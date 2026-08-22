using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace SoulTextWpf.Services;

/// <summary>
/// Owns automatic scene scheduling inside the desktop process. The persisted NextTurnAt value is
/// the source of truth; this service never invents a second delay or writes scene state itself.
/// A subsequent ScheduleAsync call atomically replaces the previous loop for the same scene.
/// </summary>
public sealed class SceneTurnScheduler : IAsyncDisposable
{
    private readonly SceneService _scenes;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _loops = new();

    public SceneTurnScheduler(SceneService scenes) => _scenes = scenes;

    public async Task ScheduleAsync(Guid sceneId, Func<Guid, CancellationToken, Task> executeDueTurn, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(executeDueTurn);
        Cancel(sceneId);

        var scene = await _scenes.GetSceneAsync(sceneId, token).ConfigureAwait(false);
        if (scene is null || !CanRunAutomatically(scene)) return;

        var source = CancellationTokenSource.CreateLinkedTokenSource(token);
        _loops[sceneId] = source;
        _ = Task.Run(() => RunAsync(sceneId, executeDueTurn, source), CancellationToken.None);
    }

    public void Cancel(Guid sceneId)
    {
        if (_loops.TryRemove(sceneId, out var source))
            source.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        var sources = _loops.ToArray();
        _loops.Clear();
        foreach (var (_, source) in sources)
            source.Cancel();
        await Task.CompletedTask;
    }

    private async Task RunAsync(Guid sceneId, Func<Guid, CancellationToken, Task> executeDueTurn, CancellationTokenSource source)
    {
        try
        {
            while (!source.IsCancellationRequested)
            {
                var scene = await _scenes.GetSceneAsync(sceneId, source.Token).ConfigureAwait(false);
                if (scene is null || !CanRunAutomatically(scene)) return;

                var dueAt = scene.NextTurnAt;
                if (dueAt is null) return;
                var delay = dueAt.Value - DateTimeOffset.Now;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, source.Token).ConfigureAwait(false);

                if (source.IsCancellationRequested) return;
                var current = await _scenes.GetSceneAsync(sceneId, source.Token).ConfigureAwait(false);
                if (current is null || !CanRunAutomatically(current) || current.NextTurnAt != dueAt) continue;

                await executeDueTurn(sceneId, source.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write($"SCENE_SCHEDULER_FAILED scene={sceneId:N}: {ex}"); }
        finally
        {
            if (_loops.TryGetValue(sceneId, out var active) && ReferenceEquals(active, source))
                _loops.TryRemove(sceneId, out _);
            source.Dispose();
        }
    }

    private static bool CanRunAutomatically(Models.SoulScene scene) =>
        ConversationTurnPolicy.CanScheduleAutomaticTurn(scene.Status, scene.TurnMode, scene.DelaySeconds);
}
