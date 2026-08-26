using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Serialises automatic cognitive maintenance. A newer user message can cancel a pending or running
/// task for its chat; all work shares one gate because a local llama-server has one practical generation lane.
/// </summary>
public sealed class CognitiveBackgroundScheduler : IAsyncDisposable
{
    private readonly SemaphoreSlim _singleGenerationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new();
    private readonly Action<string> _report;
    private int _runningCount;
    private bool _disposed;

    public CognitiveBackgroundScheduler(Action<string> report) => _report = report;
    public int RunningCount => Volatile.Read(ref _runningCount);
    public int PendingCount => Math.Max(0, _requests.Count - RunningCount);

    public void Schedule(Guid characterId, Guid chatId, string mode, int idleSeconds, Func<CancellationToken, Task> work)
    {
        var key = Key(characterId, chatId);
        Cancel(characterId, chatId);
        if (_disposed) return;
        var source = new CancellationTokenSource();
        _requests[key] = source;
        // The local model has one practical generation lane. Running automatic maintenance
        // immediately after a reply makes the next user turn queue behind it and feels like
        // the application froze. Keep the data in the persisted queue, but wait for a real
        // reading pause before consuming one maintenance batch.
        var wait = TimeSpan.FromSeconds(Math.Clamp(idleSeconds, 60, 300));
        _ = RunAsync(key, source, wait, work);
    }

    public void Cancel(Guid characterId, Guid chatId)
    {
        if (_requests.TryRemove(Key(characterId, chatId), out var source))
            source.Cancel();
    }

    private async Task RunAsync(string key, CancellationTokenSource source, TimeSpan wait, Func<CancellationToken, Task> work)
    {
        var running = false;
        try
        {
            if (wait > TimeSpan.Zero)
            {
                _report($"Память ожидает {wait.TotalSeconds:0} сек. бездействия…");
                await Task.Delay(wait, source.Token).ConfigureAwait(false);
            }
            source.Token.ThrowIfCancellationRequested();
            _report("Память обновляется в фоне…");
            Interlocked.Increment(ref _runningCount);
            running = true;
            await _singleGenerationGate.WaitAsync(source.Token).ConfigureAwait(false);
            try
            {
                source.Token.ThrowIfCancellationRequested();
                await work(source.Token).ConfigureAwait(false);
            }
            finally { _singleGenerationGate.Release(); }
        }
        catch (OperationCanceledException)
        {
            AppLog.Write($"COGNITIVE_BACKGROUND_CANCELLED key={key}");
        }
        catch (Exception exception)
        {
            AppLog.Write($"COGNITIVE_BACKGROUND_FAILED key={key}", exception);
            _report("Фоновое обновление памяти не выполнено; оно повторится после следующей паузы.");
        }
        finally
        {
            if (running) Interlocked.Decrement(ref _runningCount);
            if (_requests.TryGetValue(key, out var active) && ReferenceEquals(active, source))
                _requests.TryRemove(key, out _);
            source.Dispose();
        }
    }

    private static string Key(Guid characterId, Guid chatId) => $"{characterId:N}:{chatId:N}";

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        foreach (var request in _requests.Values) request.Cancel();
        _requests.Clear();
        _singleGenerationGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
