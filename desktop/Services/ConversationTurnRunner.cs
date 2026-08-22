using System.Collections.Concurrent;
using System.Text;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Executes a generated turn for a conversation policy. The first implementation adapts the
/// existing two-character scene format without changing persistence. It centralises the write,
/// turn switch and concurrent-run guard so UI transports do not duplicate those guarantees.
/// </summary>
public sealed class ConversationTurnRunner
{
    private readonly SceneService _scenes;
    private readonly ScenePromptEngine _scenePrompt;
    private readonly ConcurrentDictionary<Guid, byte> _activeSceneTurns = new();

    public ConversationTurnRunner(SceneService scenes, ScenePromptEngine scenePrompt)
    {
        _scenes = scenes;
        _scenePrompt = scenePrompt;
    }

    public async Task<SceneTurnResult> RunSceneTurnAsync(
        Guid sceneId,
        int contextSize,
        int reservedGenerationTokens,
        Func<IReadOnlyList<LlamaMessage>, CancellationToken, IAsyncEnumerable<string>> generate,
        Func<SoulCharacter, string, string> normalizeResponse,
        Action<SceneTurnStarted>? onStarted = null,
        Action<string>? onChunk = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(normalizeResponse);
        if (!_activeSceneTurns.TryAdd(sceneId, 0)) return SceneTurnResult.AlreadyRunning(sceneId);

        try
        {
            var runtime = await _scenes.GetRuntimeAsync(sceneId, token);
            var scene = runtime.Scene;
            if (scene.Status == "finished") return SceneTurnResult.Finished(sceneId);
            var speakerId = scene.NextCharacterId ?? scene.CharacterAId;
            var speaker = speakerId == runtime.First.Id ? runtime.First : runtime.Second;
            onStarted?.Invoke(new SceneTurnStarted(sceneId, speakerId, speaker));

            await _scenes.SetStatusAsync(sceneId, "running", speakerId, token, scheduleNextTurn: false);
            var context = _scenePrompt.Build(runtime, speakerId, contextSize, reservedGenerationTokens);
            var raw = new StringBuilder();
            await foreach (var chunk in generate(context.Messages, token).WithCancellation(token))
            {
                raw.Append(chunk);
                onChunk?.Invoke(raw.ToString());
            }

            var text = normalizeResponse(speaker, raw.ToString()).Trim();
            if (string.IsNullOrWhiteSpace(text)) text = "…";
            var saved = await _scenes.AddCharacterMessageAsync(sceneId, speakerId, text, token);
            var nextSpeakerId = speakerId == runtime.First.Id ? runtime.Second.Id : runtime.First.Id;
            var nextStatus = ConversationTurnPolicy.NextStatusAfterGeneratedTurn(scene.TurnMode);
            await _scenes.SetStatusAsync(sceneId, nextStatus, nextSpeakerId, token);
            return new SceneTurnResult(sceneId, SceneTurnExecutionStatus.Completed, speakerId, speaker.Name, saved, nextSpeakerId, nextStatus, text);
        }
        finally
        {
            _activeSceneTurns.TryRemove(sceneId, out _);
        }
    }
}

public sealed record SceneTurnStarted(Guid SceneId, Guid SpeakerCharacterId, SoulCharacter Speaker);
public enum SceneTurnExecutionStatus { Completed, AlreadyRunning, Finished }
public sealed record SceneTurnResult(
    Guid SceneId,
    SceneTurnExecutionStatus Status,
    Guid? SpeakerCharacterId,
    string SpeakerName,
    SoulSceneMessage? SavedMessage,
    Guid? NextSpeakerCharacterId,
    string NextStatus,
    string Content)
{
    public static SceneTurnResult AlreadyRunning(Guid sceneId) => new(sceneId, SceneTurnExecutionStatus.AlreadyRunning, null, "", null, null, "", "");
    public static SceneTurnResult Finished(Guid sceneId) => new(sceneId, SceneTurnExecutionStatus.Finished, null, "", null, null, "finished", "");
}
