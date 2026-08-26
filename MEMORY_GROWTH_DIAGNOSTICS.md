# Memory Growth Diagnostics

## Purpose

This document records the memory-growth investigation for SoulExe so another engineer or AI agent can continue it without repeating unsafe assumptions.

The reported symptom is:

```text
The local AI model is loaded.
The user does not send messages.
Memory appears to keep growing.
```

Do not classify this as a memory leak from Task Manager alone. First determine whether growth belongs to:

```text
SoulExe managed process
llama-server model process
GPU allocator / driver
background cognitive inference
automatic group-scene generation
mobile session retention
log-file disk growth
```

---

## Current Instrumentation

`Services/MemoryDiagnosticsSampler.cs` writes a privacy-safe snapshot every 60 seconds after `MainViewModel` initialization. The sampler stops during `MainViewModel.DisposeAsync`.

Look in:

```text
SoulExeData/logs/SoulExe.log
```

Search for:

```text
MEMORY_SNAPSHOT
```

Each snapshot contains metrics similar to:

```text
managedHeap
workingSet
privateBytes
handles
threads
llamaWs
llamaPrivate
cognitivePending
cognitiveRunning
networkSessions
```

No dialog text, prompt text, token, character name, password, or mobile session token is logged.

### Metric meaning

| Metric | Meaning | Leak signal |
|---|---|---|
| `managedHeap` | .NET managed heap in SoulExe | Continual growth after full GC opportunities and no active work is suspicious. |
| `workingSet` | RAM pages currently resident | Can fluctuate; not sufficient alone. |
| `privateBytes` | Process-private committed memory | Sustained monotonic growth is important. |
| `handles` | OS handles | Monotonic growth may indicate undisposed resources. |
| `threads` | Process threads | Continual growth can indicate timers/tasks/process leaks. |
| `llamaWs` | Working set of application-owned llama-server | Growth can be normal model/KV/compute cache allocation. |
| `llamaPrivate` | Private bytes of application-owned llama-server | Better measure for model-process growth than UI process metrics. |
| `cognitivePending` | Delayed cognitive tasks waiting for idle period | Indicates future background inference. |
| `cognitiveRunning` | Active cognitive maintenance tasks | Indicates actual model work, not passive idle. |
| `networkSessions` | Retained mobile sessions | Bounded to 256 and TTL 12 hours. |

Do not use `VirtualMemorySize` as evidence of RAM consumption. .NET and Windows may reserve a large virtual address range without committing it to physical memory.

---

## Most Likely Explanation

The most likely cause is not passive leaking. SoulExe intentionally schedules Cognitive Architecture after a reply:

```text
reply completes
→ wait 60 to 300 seconds
→ summary and/or Soul Memory maintenance
→ one or more real LLM requests
→ llama.cpp allocates or expands KV/compute/GPU caches
→ allocator keeps these buffers for reuse
```

Relevant code:

```text
Services/CognitiveBackgroundScheduler.cs
ViewModels/MainViewModel.Chat.Messaging.cs
ViewModels/MainViewModel.Network.cs
ViewModels/MainViewModel.Memory.Cognitive.cs
Services/SoulMemoryService.cs
```

The `full` Soul Memory path can execute multiple LLM requests in one idle window:

```text
Router
up to five Archivist operations
Diary update
```

This means "user is not typing" does not necessarily mean "the model is idle".

---

## Other Known Background Work

| Source | Conditions | Relevant files |
|---|---|---|
| Cognitive maintenance | After a chat response | `CognitiveBackgroundScheduler.cs`, `MainViewModel.Memory.Cognitive.cs` |
| Auto scene turns | A scene has `Running` state and future `NextTurnAt` | `SceneTurnScheduler.cs`, `MainViewModel.Scenes.Turns.cs` |
| Group summary | After group turns | `MainViewModel.Scenes.Turns.cs` |
| Mobile polling | Mobile web client open | `MobileStyleWebClient.cs` |
| Streaming preview | Only while generation is active | `StreamingPreviewPublisher.cs` |

---

## llama.cpp Memory Behavior

`llama-server.exe` is a separate process started by `LlamaServerService`.

Relevant arguments include:

```text
-c / context size
--parallel
-ngl / GPU layers
--flash-attn
--mlock
--no-mmap
--cache-type-k
--cache-type-v
batch size
```

Relevant files:

```text
Services/LlamaServerService.cs
ViewModels/LlamaRuntimeOptions.cs
ViewModels/LlamaSettingsFactory.cs
```

Normal behavior after loading a model or the first request:

```text
model GGUF allocation
KV cache allocation
compute graph buffers
tokenizer/template caches
CUDA/Vulkan allocator reservation
```

These buffers may remain allocated until llama-server is stopped. A plateau is normally cache reuse, not a leak.

### Things that amplify llama-server memory

```text
Large context size
Large --parallel value
f16 K/V cache
High GPU layer count
--mlock
--no-mmap
First cognitive-maintenance inference after initial user reply
```

---

## Measurement Protocol

### A. Baseline

Before model load, record:

```text
SoulExe.exe: private bytes, working set, handles, threads, managed heap
llama-server.exe: private bytes, working set, handles, threads
GPU dedicated/shared memory if applicable
```

### B. Model load without requests

1. Start SoulExe.
2. Load the model.
3. Do not send a message for 10 minutes.
4. Compare `MEMORY_SNAPSHOT` rows.

Interpretation:

```text
SoulExe stable + llama stable
→ no active issue observed.

SoulExe stable + llama grows then plateaus
→ expected model/cache allocation.

SoulExe grows with no COGNITIVE/GEN/SCENE log entries
→ investigate managed/WPF retention.
```

### C. Controlled response and idle window

1. Send exactly one short message.
2. Note the response completion timestamp.
3. Observe logs for 6 minutes.
4. Search for:

```text
COGNITIVE_
SOUL_MEMORY_
GEN
SCENE
```

If these appear, correlate their timestamps with `llamaWs`, `llamaPrivate`, `managedHeap`, and `privateBytes`.

### D. Isolate cognitive maintenance

For one test character only, temporarily disable through UI:

```text
Auto Summary
Soul Memory
```

Do not delete data. Repeat the controlled response and idle window.

If memory growth disappears or changes to a stable plateau, the observed growth is connected to background inference rather than passive UI retention.

### E. Auto scenes

Before measuring, pause every scene that may be running.

Search logs for:

```text
scene_
NETWORK_SCENE_BEGIN
NETWORK_SCENE_SAVED
```

An automatically running scene is not idle.

### F. Repeatability test

Run five identical short requests:

```text
baseline
after model load
after request 1
after request 5
after 10 minutes idle
```

Expected cache pattern:

```text
increase after first or second request
→ plateau
```

Possible leak pattern:

```text
increase after every equivalent request
→ no plateau
→ persists through idle
```

---

## Decision Matrix

| Observation | Likely conclusion | Next action |
|---|---|---|
| `llamaPrivate` rises, SoulExe `managedHeap` stays flat, then plateau | Normal llama cache/model allocation | Tune context/parallel/KV cache only if memory budget requires it. |
| `llamaPrivate` rises with `COGNITIVE_*` logs | Background inference is working | Decide product policy for cognitive maintenance; do not call it a leak without a plateau test. |
| `llamaPrivate` rises with scene logs | Auto scene generation | Pause scene or inspect scheduler settings. |
| SoulExe `managedHeap` and `privateBytes` rise with `cognitiveRunning=0`, no GEN/SCENE logs | Possible managed/WPF leak | Take two managed heap dumps and compare roots. |
| Handles or threads rise continuously | Resource/task leak candidate | Inspect timers, Process objects, Http responses, event handlers, and task lifecycle. |
| `networkSessions` rises toward 256 | Mobile session traffic | Inspect mobile logins; store is bounded by TTL/cap. |
| Only log file grows | Disk log activity, not RAM leak | Inspect log rotation; current cap is 5 MiB plus four archives. |

---

## Existing Retention Guards

### Mobile sessions

`NetworkSessionStore` now limits retained sessions:

```text
Sliding TTL: 12 hours
Maximum sessions: 256
```

Password changes and server stop still invalidate all sessions.

### Application log

`AppLog` now rotates:

```text
Current log maximum: 5 MiB
Archives retained: 4
Files: SoulExe.log, SoulExe.1.log ... SoulExe.4.log
```

This prevents disk growth, not memory growth.

### UI page cache

`AppShellView` caches a bounded set of primary views. This is expected retention:

```text
one instance per visited primary page
```

It is not unbounded by itself. Do not remove page caching without heap evidence; it preserves navigation state and avoids repeated expensive XAML construction.

---

## If a Managed Leak Is Confirmed

Do not make broad changes first.

1. Capture two managed heap dumps 10 to 15 minutes apart.
2. Compare retained types and roots.
3. Start with:

```text
FlowDocument
Run
Paragraph
ChatMessageViewModel
SceneMessageViewModel
DispatcherOperation
DispatcherTimer
string
SoulMemorySnapshot
SoulDiaryEntry
event handlers
```

4. Determine whether the root is:

```text
static collection
event subscription
timer
cached view
pending task
network session
native interop handle
```

5. Apply a narrow fix only after the retaining root is known.

Useful tools:

```powershell
dotnet-counters monitor --process-id <SoulExePid> System.Runtime
dotnet-gcdump collect --process-id <SoulExePid>
```

Visual Studio Diagnostic Tools, PerfView, and JetBrains dotMemory can compare managed heap snapshots.

---

## Rules for Future Changes

- Do not disable Cognitive Architecture solely because working set grows once.
- Do not use `VirtualMemorySize` as an RAM-leak metric.
- Distinguish `SoulExe.exe` from `llama-server.exe` in every measurement.
- Correlate memory snapshots with `GEN`, `COGNITIVE`, `SOUL_MEMORY`, and `SCENE` log events.
- Any periodic diagnostics service must be lifecycle-bound and disposed.
- Any cache/session/log store must have explicit TTL, size cap, or bounded ownership.
- Do not add memory sampling that records dialog text, prompts, names, passwords, tokens, or session IDs.
- Do not change model memory options (`context`, `parallel`, cache type, mmap, mlock) without recording baseline and plateau metrics.

---

## Current Verification

The following checks passed when this document was created:

```powershell
dotnet build SoulExe.csproj --no-restore -warnaserror
dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj --no-restore
```

`SoulExe.ConversationChecks` includes checks for:

```text
memory sampler single lifecycle and disposal
mobile session TTL and maximum count
log rotation and bounded archive count
```
