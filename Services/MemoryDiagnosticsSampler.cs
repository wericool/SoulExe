using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SoulExe.Services;

public sealed record ProcessMemoryDiagnostic(long ManagedHeapBytes, long WorkingSetBytes, long PrivateBytes, int Handles, int Threads);

public sealed record MemoryDiagnosticSnapshot(ProcessMemoryDiagnostic SoulExe, int? LlamaProcessId, ProcessMemoryDiagnostic? Llama, int CognitivePending, int CognitiveRunning, int NetworkSessions);

/// <summary>Emits low-frequency aggregate process counters and owns exactly one cancellable sampling loop.</summary>
public sealed class MemoryDiagnosticsSampler : IAsyncDisposable
{
    private readonly Func<MemoryDiagnosticSnapshot> _snapshot;
    private readonly Action<string> _write;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public MemoryDiagnosticsSampler(Func<MemoryDiagnosticSnapshot> snapshot, Action<string> write, TimeSpan? interval = null)
    {
        _snapshot = snapshot;
        _write = write;
        _interval = interval ?? TimeSpan.FromMinutes(1);
    }

    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _loop = RunAsync(_cancellation.Token);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                WriteSnapshot();
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { AppLog.Write("MEMORY_DIAGNOSTICS_FAILED", exception); }
    }

    private void WriteSnapshot()
    {
        var snapshot = _snapshot();
        var soul = snapshot.SoulExe;
        var llama = snapshot.Llama is { } child
            ? $" llamaPid={snapshot.LlamaProcessId} llamaWs={child.WorkingSetBytes} llamaPrivate={child.PrivateBytes} llamaHandles={child.Handles} llamaThreads={child.Threads}"
            : " llamaPid=none";
        _write($"MEMORY_SNAPSHOT managedHeap={soul.ManagedHeapBytes} workingSet={soul.WorkingSetBytes} privateBytes={soul.PrivateBytes} handles={soul.Handles} threads={soul.Threads}{llama} cognitivePending={snapshot.CognitivePending} cognitiveRunning={snapshot.CognitiveRunning} networkSessions={snapshot.NetworkSessions}");
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            loop = _loop;
            cancellation = _cancellation;
            _loop = null;
            _cancellation = null;
        }
        if (cancellation is null) return;
        cancellation.Cancel();
        try { if (loop is not null) await loop.ConfigureAwait(false); }
        finally { cancellation.Dispose(); }
    }

    public static MemoryDiagnosticSnapshot Capture(int? llamaProcessId, int cognitivePending, int cognitiveRunning, int networkSessions)
    {
        using var soulProcess = Process.GetCurrentProcess();
        var soul = CaptureProcess(soulProcess, includeManagedHeap: true);
        ProcessMemoryDiagnostic? llama = null;
        if (llamaProcessId is { } processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited) llama = CaptureProcess(process, includeManagedHeap: false);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        return new MemoryDiagnosticSnapshot(soul, llamaProcessId, llama, cognitivePending, cognitiveRunning, networkSessions);
    }

    private static ProcessMemoryDiagnostic CaptureProcess(Process process, bool includeManagedHeap) => new(
        includeManagedHeap ? GC.GetTotalMemory(forceFullCollection: false) : 0,
        process.WorkingSet64,
        process.PrivateMemorySize64,
        process.HandleCount,
        process.Threads.Count);
}
