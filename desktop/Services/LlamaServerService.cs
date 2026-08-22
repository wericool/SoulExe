using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class LlamaServerService : IAsyncDisposable
{
    // Russian text and chat-template wrappers often tokenize much denser than the old 4-char rule.
    // Keep a guard at the HTTP boundary because it is the only common path for chats, scenes and summaries.
    private const int ContextProtocolReserveTokens = 768;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object _diagnosticGate = new();
    private readonly List<string> _recentOutput = [];
    private Process? _process;

    public bool IsStartedByApplication => _process is { HasExited: false };
    public string LastLaunchDiagnostic { get; private set; } = "Модель ещё не запускалась.";
    public string LastLaunchCommand { get; private set; } = "";
    public int? ProcessId => _process is { HasExited: false } process ? process.Id : null;

    public async Task<bool> IsAvailableAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"http://{settings.PreferredHost}:{settings.LlamaPort}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task StartAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ResetDiagnostics();
        if (await IsAvailableAsync(settings, cancellationToken).ConfigureAwait(false))
        {
            LastLaunchDiagnostic = $"API llama.cpp уже доступен по адресу http://{settings.PreferredHost}:{settings.LlamaPort}.";
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.LlamaServerPath) || !File.Exists(settings.LlamaServerPath))
            throw new InvalidOperationException("Не найден llama-server.exe. Нажмите «Установить llama.cpp (CPU)» или укажите путь к llama-server.exe.");

        var hasLocalModel = !string.IsNullOrWhiteSpace(settings.ModelPath) && File.Exists(settings.ModelPath);
        var hasHostedModel = !string.IsNullOrWhiteSpace(settings.ModelHuggingFaceRepository);
        if (!hasLocalModel && !hasHostedModel)
            throw new InvalidOperationException("Не выбрана GGUF-модель. Выберите локальный файл или модель из Models Hub.");
        if (_process is { HasExited: false })
        {
            LastLaunchDiagnostic = $"llama-server.exe уже запущен (PID {_process.Id}). Ожидаю доступность API…";
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = settings.LlamaServerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(settings.LlamaServerPath) ?? Environment.CurrentDirectory
        };
        if (hasLocalModel)
        {
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add(settings.ModelPath);
        }
        else
        {
            startInfo.ArgumentList.Add("-hf");
            startInfo.ArgumentList.Add(settings.ModelHuggingFaceRepository);
        }
        startInfo.ArgumentList.Add("--host"); startInfo.ArgumentList.Add(settings.PreferredHost);
        startInfo.ArgumentList.Add("--port"); startInfo.ArgumentList.Add(settings.LlamaPort.ToString());
        startInfo.ArgumentList.Add("--parallel"); startInfo.ArgumentList.Add(Math.Clamp(settings.ParallelSlots, 1, 16).ToString());
        startInfo.ArgumentList.Add("-c"); startInfo.ArgumentList.Add(Math.Max(1024, settings.ContextSize).ToString());
        if (!settings.ReasoningMode) { startInfo.ArgumentList.Add("--reasoning-budget"); startInfo.ArgumentList.Add("0"); startInfo.ArgumentList.Add("--reasoning"); startInfo.ArgumentList.Add("off"); }
        else if (settings.ReasoningBudget >= 0) { startInfo.ArgumentList.Add("--reasoning-budget"); startInfo.ArgumentList.Add(settings.ReasoningBudget.ToString()); }
        if (!string.IsNullOrWhiteSpace(settings.ChatTemplate) && !string.Equals(settings.ChatTemplate, "auto", StringComparison.OrdinalIgnoreCase)) { startInfo.ArgumentList.Add("--chat-template"); startInfo.ArgumentList.Add(settings.ChatTemplate.Trim()); }
        else startInfo.ArgumentList.Add("--jinja");
        if (settings.GpuLayers > 0) { startInfo.ArgumentList.Add("-ngl"); startInfo.ArgumentList.Add(settings.GpuLayers.ToString()); }
        if (settings.FlashAttention) { startInfo.ArgumentList.Add("--flash-attn"); startInfo.ArgumentList.Add("auto"); }
        if (settings.UseMlock) startInfo.ArgumentList.Add("--mlock");
        if (!settings.UseMmap) startInfo.ArgumentList.Add("--no-mmap");
        if (!string.IsNullOrWhiteSpace(settings.KvCacheType) && !string.Equals(settings.KvCacheType, "f16", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("--cache-type-k"); startInfo.ArgumentList.Add(settings.KvCacheType);
            startInfo.ArgumentList.Add("--cache-type-v"); startInfo.ArgumentList.Add(settings.KvCacheType);
        }
        if (settings.CpuThreads > 0) { startInfo.ArgumentList.Add("--threads"); startInfo.ArgumentList.Add(settings.CpuThreads.ToString()); startInfo.ArgumentList.Add("--threads-batch"); startInfo.ArgumentList.Add(settings.CpuThreads.ToString()); }
        if (settings.BatchSize > 0) { startInfo.ArgumentList.Add("--batch-size"); startInfo.ArgumentList.Add(settings.BatchSize.ToString()); startInfo.ArgumentList.Add("--ubatch-size"); startInfo.ArgumentList.Add(settings.BatchSize.ToString()); }
        if (settings.CpuMoeLayers > 0) { startInfo.ArgumentList.Add("--n-cpu-moe"); startInfo.ArgumentList.Add(settings.CpuMoeLayers.ToString()); }
        foreach (var argument in SplitArguments(settings.ExtraArguments)) startInfo.ArgumentList.Add(argument);
        LastLaunchCommand = FormatCommand(startInfo.FileName, startInfo.ArgumentList);
        LastLaunchDiagnostic = "Процесс llama.cpp подготовлен. Запускаю…";
        AppLog.Write($"Starting llama.cpp: {LastLaunchCommand}");

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Windows не удалось создать процесс llama-server.exe.");
        var stdoutTask = DrainAsync(_process.StandardOutput, "stdout");
        var stderrTask = DrainAsync(_process.StandardError, "stderr");
        LastLaunchDiagnostic = $"llama-server.exe запущен (PID {_process.Id}). Ожидание API на порту {settings.LlamaPort}…";

        for (var attempt = 0; attempt < 75; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(400, cancellationToken);
            if (await IsAvailableAsync(settings, cancellationToken))
            {
                LastLaunchDiagnostic = $"API готов: http://{settings.PreferredHost}:{settings.LlamaPort}.\n{GetRecentOutput()}";
                AppLog.Write("llama.cpp API became available.");
                return;
            }
            if (_process.HasExited)
            {
                await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(1000, cancellationToken));
                var exitCode = _process.ExitCode;
                var details = GetRecentOutput();
                LastLaunchDiagnostic = $"llama-server.exe завершился с кодом {exitCode}.\n{details}";
                AppLog.Write($"llama.cpp exited before API; code {exitCode}.\n{details}");
                throw new InvalidOperationException($"llama-server.exe завершился с кодом {exitCode} до запуска API. Последние сообщения движка:\n{details}");
            }
        }
        var timeoutDetails = GetRecentOutput();
        LastLaunchDiagnostic = $"API не ответил за 30 секунд.\n{timeoutDetails}";
        throw new TimeoutException($"Локальная LLM не ответила за 30 секунд. Последние сообщения движка:\n{timeoutDetails}");
    }

    public async IAsyncEnumerable<string> GenerateAsync(AppSettings settings, CharacterProfile character, IReadOnlyCollection<ChatMessageRecord> history, string userMessage, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = new List<LlamaMessage> { new("system", character.SystemPrompt) };
        messages.AddRange(history.TakeLast(30).Select(x => new LlamaMessage(x.Role == ChatRole.User ? "user" : "assistant", x.Content)));
        messages.Add(new LlamaMessage("user", userMessage));
        await foreach (var chunk in GenerateFromMessagesAsync(settings, messages, cancellationToken)) yield return chunk;
    }

    public async IAsyncEnumerable<string> GenerateFromMessagesAsync(AppSettings settings, IReadOnlyList<LlamaMessage> messages, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, string? diagnosticId = null)
    {
        if (!await IsAvailableAsync(settings, cancellationToken)) throw new InvalidOperationException("Локальная модель не запущена. Нажмите «Запустить модель».");
        var traceId = string.IsNullOrWhiteSpace(diagnosticId) ? Guid.NewGuid().ToString("N")[..12] : diagnosticId;
        var stopwatch = Stopwatch.StartNew();
        var stop = settings.StopStrings.Split(new[] { '\n', '|' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var requestMessages = messages.ToList();
        if (!settings.ReasoningMode)
        {
            requestMessages.Insert(0, new LlamaMessage("system", "[DIRECT RESPONSE MODE] Answer directly and concisely. Do not produce internal chain-of-thought, <think> blocks, hidden reasoning, or analysis before the answer. Preserve the roleplay style and answer only with the final in-character response."));
        }
        var contextLimit = Math.Max(1024, settings.ContextSize);
        var effectiveMaxTokens = Math.Clamp(settings.MaxTokens, 64, Math.Max(64, contextLimit - ContextProtocolReserveTokens));
        var promptBudget = Math.Max(256, contextLimit - effectiveMaxTokens - ContextProtocolReserveTokens);
        var fitted = FitMessagesToContext(requestMessages, promptBudget);
        requestMessages = fitted.Messages;
        if (fitted.WasTrimmed)
        {
            AppLog.Write($"GEN {traceId} CONTEXT_TRIM sourceMessages={fitted.OriginalMessageCount} sentMessages={requestMessages.Count} estimatedBefore={fitted.OriginalEstimatedTokens} estimatedAfter={fitted.FinalEstimatedTokens} promptBudget={promptBudget} contextLimit={contextLimit} maxTokens={effectiveMaxTokens}");
        }

        var payload = new
        {
            messages = requestMessages,
            stream = true,
            reasoning = settings.ReasoningMode,
            temperature = settings.Temperature,
            top_p = settings.TopP,
            top_k = settings.TopK,
            min_p = settings.EnableAdvancedSampling ? settings.MinP : 0d,
            repeat_penalty = settings.RepeatPenalty,
            frequency_penalty = settings.FrequencyPenalty,
            presence_penalty = settings.PresencePenalty,
            dynatemp_range = settings.EnableAdvancedSampling && settings.DynamicTemperatureMax > settings.DynamicTemperatureMin ? settings.DynamicTemperatureMax - settings.DynamicTemperatureMin : 0d,
            dynatemp_exponent = settings.EnableAdvancedSampling && settings.DynamicTemperatureMax > settings.DynamicTemperatureMin ? settings.DynamicTemperatureExponent : 1d,
            xtc_probability = settings.EnableAdvancedSampling ? settings.XtcProbability : 0d,
            xtc_threshold = settings.EnableAdvancedSampling ? settings.XtcThreshold : 0d,
            dry_multiplier = settings.EnableAdvancedSampling ? settings.DryMultiplier : 0d,
            dry_base = settings.EnableAdvancedSampling ? settings.DryBase : 1.75d,
            dry_allowed_length = settings.EnableAdvancedSampling ? settings.DryAllowedLength : 2,
            stop,
            max_tokens = effectiveMaxTokens
        };
        var requestFingerprint = AppLog.Fingerprint(string.Join("\n", requestMessages.Select(message => $"{message.role}:{message.content}")));
        AppLog.Write($"GEN {traceId} HTTP_REQUEST endpoint=http://{settings.PreferredHost}:{settings.LlamaPort}/v1/chat/completions messages={requestMessages.Count} promptEstimate={fitted.FinalEstimatedTokens} promptBudget={promptBudget} contextLimit={contextLimit} promptHash={requestFingerprint} temperature={settings.Temperature:0.###} topP={settings.TopP:0.###} topK={settings.TopK} minP={payload.min_p:0.###} dynTempRange={payload.dynatemp_range:0.###} dynTempExponent={payload.dynatemp_exponent:0.###} maxTokens={effectiveMaxTokens} reasoning={settings.ReasoningMode} stopCount={stop.Length}");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{settings.PreferredHost}:{settings.LlamaPort}/v1/chat/completions") { Content = JsonContent.Create(payload) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        AppLog.Write($"GEN {traceId} HTTP_RESPONSE status={(int)response.StatusCode} reason={response.ReasonPhrase} elapsedMs={stopwatch.ElapsedMilliseconds} contentType={response.Content.Headers.ContentType}");
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Локальная модель вернула ошибку {(int)response.StatusCode}: {error}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var receivedText = new StringBuilder();
        var dataEvents = 0;
        var contentChunks = 0;
        var malformedEvents = 0;
        var doneReceived = false;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ", StringComparison.Ordinal)) continue;
            dataEvents++;
            var data = line[6..].Trim();
            if (data == "[DONE]") { doneReceived = true; break; }
            string? delta = null;
            try { using var document = JsonDocument.Parse(data); delta = document.RootElement.GetProperty("choices")[0].GetProperty("delta").TryGetProperty("content", out var content) ? content.GetString() : null; }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                malformedEvents++;
                if (malformedEvents <= 2) AppLog.Write($"GEN {traceId} SSE_PARSE_WARNING event={dataEvents} dataPreview=«{AppLog.Preview(data, 160)}»");
            }
            if (!string.IsNullOrEmpty(delta))
            {
                contentChunks++;
                receivedText.Append(delta);
                yield return delta;
            }
        }
        stopwatch.Stop();
        AppLog.Write($"GEN {traceId} HTTP_STREAM_END done={doneReceived} dataEvents={dataEvents} contentChunks={contentChunks} malformed={malformedEvents} chars={receivedText.Length} hash={AppLog.Fingerprint(receivedText.ToString())} elapsedMs={stopwatch.ElapsedMilliseconds} preview=«{AppLog.Preview(receivedText.ToString())}»");
    }

    public async Task StopAsync()
    {
        if (_process is not { HasExited: false } process) return;
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        _process.Dispose();
        _process = null;
        LastLaunchDiagnostic = "llama-server.exe остановлен пользователем.";
    }

    private void ResetDiagnostics()
    {
        lock (_diagnosticGate) _recentOutput.Clear();
        LastLaunchCommand = "";
        LastLaunchDiagnostic = "Подготавливается запуск llama.cpp…";
    }

    private async Task DrainAsync(StreamReader reader, string channel)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                lock (_diagnosticGate)
                {
                    _recentOutput.Add($"[{channel}] {line}");
                    if (_recentOutput.Count > 30) _recentOutput.RemoveRange(0, _recentOutput.Count - 30);
                }
                AppLog.Write($"llama.cpp {channel}: {line}");
            }
        }
        catch (Exception ex) { AppLog.Write($"Не удалось прочитать поток llama.cpp {channel}.", ex); }
    }

    private string GetRecentOutput()
    {
        lock (_diagnosticGate) return _recentOutput.Count == 0 ? "llama.cpp не вывел диагностических строк." : string.Join(Environment.NewLine, _recentOutput.TakeLast(12));
    }

    private static ContextFitResult FitMessagesToContext(IReadOnlyList<LlamaMessage> messages, int promptBudget)
    {
        var source = messages ?? [];
        var originalEstimate = EstimateMessages(source);
        if (originalEstimate <= promptBudget)
            return new ContextFitResult(source.ToList(), source.Count, originalEstimate, originalEstimate, false);

        // Preserve only the leading system instructions as fixed context. Director events in a scene
        // deliberately belong to history, otherwise many old system-role events could defeat trimming.
        var leadingSystemCount = 0;
        while (leadingSystemCount < source.Count && string.Equals(source[leadingSystemCount].role, "system", StringComparison.OrdinalIgnoreCase))
            leadingSystemCount++;

        var hasFinalTurn = source.Count > leadingSystemCount;
        var finalTurn = hasFinalTurn ? source[^1] : null;
        var leading = source.Take(leadingSystemCount).ToList();
        var historyEnd = hasFinalTurn ? source.Count - 1 : source.Count;
        var protectedEstimate = EstimateMessages(leading) + (finalTurn is null ? 0 : EstimateMessage(finalTurn));
        var trimmed = false;

        if (protectedEstimate > promptBudget)
        {
            var finalBudget = finalTurn is null ? 0 : Math.Max(96, Math.Min(EstimateMessage(finalTurn), promptBudget / 3));
            if (finalTurn is not null && EstimateMessage(finalTurn) > finalBudget)
            {
                finalTurn = finalTurn with { content = TrimToBudget(finalTurn.content, finalBudget) };
                trimmed = true;
            }

            var systemBudget = Math.Max(128, promptBudget - (finalTurn is null ? 0 : EstimateMessage(finalTurn)));
            var rebuiltLeading = new List<LlamaMessage>();
            var remainingBudget = systemBudget;
            for (var index = 0; index < leading.Count; index++)
            {
                var remainingItems = leading.Count - index;
                var itemBudget = Math.Max(48, remainingBudget / Math.Max(1, remainingItems));
                var original = leading[index];
                var compact = EstimateMessage(original) > itemBudget
                    ? original with { content = TrimToBudget(original.content, itemBudget) }
                    : original;
                if (!string.Equals(compact.content, original.content, StringComparison.Ordinal)) trimmed = true;
                rebuiltLeading.Add(compact);
                remainingBudget = Math.Max(0, remainingBudget - EstimateMessage(compact));
            }
            leading = rebuiltLeading;
        }

        var acceptedHistory = new List<LlamaMessage>();
        var spent = EstimateMessages(leading) + (finalTurn is null ? 0 : EstimateMessage(finalTurn));
        for (var index = historyEnd - 1; index >= leadingSystemCount; index--)
        {
            var message = source[index];
            var cost = EstimateMessage(message);
            if (spent + cost > promptBudget) continue;
            acceptedHistory.Add(message);
            spent += cost;
        }
        acceptedHistory.Reverse();

        var result = new List<LlamaMessage>(leading.Count + acceptedHistory.Count + (finalTurn is null ? 0 : 1));
        result.AddRange(leading);
        result.AddRange(acceptedHistory);
        if (finalTurn is not null) result.Add(finalTurn);
        var finalEstimate = EstimateMessages(result);
        return new ContextFitResult(result, source.Count, originalEstimate, finalEstimate, trimmed || result.Count != source.Count);
    }

    private static int EstimateMessages(IEnumerable<LlamaMessage> messages) => messages.Sum(EstimateMessage);

    private static int EstimateMessage(LlamaMessage message) => Math.Max(12, ((message.content?.Length ?? 0) + 1) / 2 + 12);

    private static string TrimToBudget(string value, int tokenBudget)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var maximumCharacters = Math.Max(48, tokenBudget * 2 - 24);
        return value.Length <= maximumCharacters ? value : value[..maximumCharacters].TrimEnd() + "…";
    }

    private sealed record ContextFitResult(List<LlamaMessage> Messages, int OriginalMessageCount, int OriginalEstimatedTokens, int FinalEstimatedTokens, bool WasTrimmed);

    private static string FormatCommand(string executable, ICollection<string> arguments) => executable + " " + string.Join(" ", arguments.Select(x => x.Contains(' ') ? $"\"{x}\"" : x));

    private static IReadOnlyList<string> SplitArguments(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<string>(); var current = new StringBuilder(); var quoted = false;
        foreach (var character in value)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } continue; }
            current.Append(character);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    public async ValueTask DisposeAsync() { await StopAsync(); _http.Dispose(); }
}
