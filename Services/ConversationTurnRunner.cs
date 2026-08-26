using System.Collections.Concurrent;
using System.Text;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Executes a generated turn for personal and group conversations.
/// Prompt building is shared via <see cref="ConversationPromptEngine"/>; persistence
/// and concurrent-run guards live here so UI and network transports stay thin.
/// </summary>
public sealed class ConversationTurnRunner
{
    private readonly ConversationPromptEngine _prompt;
    private readonly JsonDataStore _store;
    private readonly ConcurrentDictionary<Guid, byte> _activeGroupTurns = new();
    private readonly ConcurrentDictionary<Guid, byte> _activePersonalTurns = new();

    public ConversationTurnRunner(
        ConversationPromptEngine prompt,
        JsonDataStore? store = null)
    {
        _prompt = prompt;
        _store = store ?? AppServices.DataStore;
    }

    // -------------------------------------------------------------------------
    // Personal conversation (user ↔ one character)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the direct-chat prompt, streams generation, runs <paramref name="finalizeAsync"/>
    /// (state extract + presentation normalize), and optionally persists the assistant reply.
    /// The caller is responsible for persisting the user message before invoking a user turn.
    /// </summary>
    public async Task<DirectTurnResult> RunPersonalTurnAsync(
        Guid characterId,
        Guid chatId,
        string userMessage,
        bool isContinuation,
        int contextSize,
        int reservedGenerationTokens,
        Func<IReadOnlyList<LlamaMessage>, CancellationToken, IAsyncEnumerable<string>> generate,
        Func<string, CancellationToken, Task<string>> finalizeAsync,
        Action<string>? onChunk = null,
        bool persistAssistant = true,
        Guid? activePersonaId = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(generate);
        ArgumentNullException.ThrowIfNull(finalizeAsync);
        if (!_activePersonalTurns.TryAdd(chatId, 0))
            return DirectTurnResult.AlreadyRunning(characterId, chatId);

        var generationId = Guid.NewGuid().ToString("N")[..12];
        try
        {
            var mode = isContinuation ? "continuation" : "user_turn";
            var commandLog = isContinuation
                ? "directorCommand=«*continue*»"
                : $"userLen={userMessage.Length} userHash={AppLog.Fingerprint(userMessage)}";
            AppLog.Write($"GEN {generationId} BEGIN mode={mode} character={characterId} chat={chatId} {commandLog}");

            var context = await BuildDirectPromptAsync(
                characterId, chatId, userMessage, isContinuation, contextSize, reservedGenerationTokens, activePersonaId, token);

            LogPromptBuild(generationId, context);

            var raw = new StringBuilder();
            var chunkCount = 0;
            await foreach (var chunk in generate(context.Messages, token).WithCancellation(token))
            {
                chunkCount++;
                raw.Append(chunk);
                onChunk?.Invoke(raw.ToString());
            }
            AppLog.Write($"GEN {generationId} STREAM_CONSUMED chunks={chunkCount} chars={raw.Length}");

            var rawText = raw.ToString();
            if (string.IsNullOrWhiteSpace(rawText)) rawText = "Модель не вернула текст.";

            var finalText = (await finalizeAsync(rawText, token)).Trim();
            if (string.IsNullOrWhiteSpace(finalText)) finalText = "Модель не вернула текст.";

            SoulMessage? saved = null;
            string characterName = "";
            if (persistAssistant)
            {
                var character = await _store.ReadAsync(root => (root.Characters ?? []).FirstOrDefault(value => value.Id == characterId), token)
                    ?? throw new InvalidOperationException("Персонаж не найден.");
                characterName = character.Name;
                saved = await AppendPersonalCharacterMessageAsync(chatId, characterId, character.Name, character.AvatarPath, finalText, token);
            }
            else
            {
                var character = await _store.ReadAsync(root => (root.Characters ?? []).FirstOrDefault(value => value.Id == characterId), token);
                characterName = character?.Name ?? "";
            }

            return new DirectTurnResult(
                characterId,
                chatId,
                DirectTurnExecutionStatus.Completed,
                characterName,
                saved,
                finalText,
                rawText);
        }
        finally
        {
            _activePersonalTurns.TryRemove(chatId, out _);
        }
    }

    /// <summary>
    /// Streams only the model tokens for a direct turn (no finalize / persist).
    /// Used when the UI layer still owns presentation state machines.
    /// </summary>
    public async IAsyncEnumerable<string> StreamPersonalTurnAsync(
        Guid characterId,
        Guid chatId,
        string userMessage,
        bool isContinuation,
        int contextSize,
        int reservedGenerationTokens,
        Func<IReadOnlyList<LlamaMessage>, CancellationToken, IAsyncEnumerable<string>> generate,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(generate);
        if (!_activePersonalTurns.TryAdd(chatId, 0))
            yield break;

        var generationId = Guid.NewGuid().ToString("N")[..12];
        try
        {
            var mode = isContinuation ? "continuation" : "user_turn";
            AppLog.Write($"GEN {generationId} BEGIN mode={mode} character={characterId} chat={chatId} streamOnly=1");

            var context = await BuildDirectPromptAsync(
                characterId, chatId, userMessage, isContinuation, contextSize, reservedGenerationTokens, null, token);

            LogPromptBuild(generationId, context);

            var chunkCount = 0;
            var outputLength = 0;
            await foreach (var chunk in generate(context.Messages, token).WithCancellation(token))
            {
                chunkCount++;
                outputLength += chunk.Length;
                yield return chunk;
            }
            AppLog.Write($"GEN {generationId} STREAM_CONSUMED chunks={chunkCount} chars={outputLength}");
        }
        finally
        {
            _activePersonalTurns.TryRemove(chatId, out _);
        }
    }

    private async Task<PromptBuildResult> BuildDirectPromptAsync(
        Guid characterId,
        Guid chatId,
        string userMessage,
        bool isContinuation,
        int contextSize,
        int reservedGenerationTokens,
        Guid? activePersonaId,
        CancellationToken token)
    {
        return await _store.ReadAsync(root =>
        {
            var conversation = (root.Conversations ?? []).FirstOrDefault(value => value.Id == chatId && value.Mode == ConversationMode.Personal)
                ?? throw new InvalidOperationException("Личный разговор не найден.");
            var participantCharacterId = conversation.Participants
                .Where(participant => participant.Kind == ConversationParticipantKind.Character)
                .OrderBy(participant => participant.SortOrder)
                .Select(participant => participant.CharacterId)
                .FirstOrDefault();
            if (participantCharacterId != characterId)
                throw new InvalidOperationException("Персонаж не является участником личного разговора.");
            var storedCharacter = root.Characters?.FirstOrDefault(x => x is not null && x.Id == participantCharacterId)
                ?? throw new InvalidOperationException("Персонаж не найден.");
            storedCharacter.LorebookIds ??= [];
            conversation.Context.Memory ??= new SoulMemoryBundle();
            conversation.Context.Memory.Topics ??= [];

            var personaId = activePersonaId ?? storedCharacter.SelectedPersonaId;
            var persona = personaId is null
                ? null
                : root.Personas?.FirstOrDefault(x => x is not null && x.Id == personaId);
            var preset = storedCharacter.SelectedPromptPresetId is null
                ? null
                : root.PromptPresets?.FirstOrDefault(x => x is not null && x.Id == storedCharacter.SelectedPromptPresetId);
            var books = (root.Lorebooks ?? []).Where(x => x is not null && storedCharacter.LorebookIds.Contains(x.Id)).ToList();
            var promptUserMessage = isContinuation ? string.Empty : userMessage;
            var topics = MemoryTopicSelector.Select(conversation.Context.Memory.Topics, promptUserMessage);

            return _prompt.BuildDirect(new PromptBuildRequest(
                storedCharacter,
                conversation,
                persona,
                preset,
                books,
                topics,
                promptUserMessage,
                contextSize,
                reservedGenerationTokens,
                IncludeSoulMemory: storedCharacter.CognitiveArchitectureEnabled && storedCharacter.SoulMemoryEnabled,
                IncludeAutoSummary: storedCharacter.CognitiveArchitectureEnabled && storedCharacter.AutoSummaryEnabled,
                // The message has already been persisted with its author kind. Keeping it
                // in history prevents a Director event from being re-added as a user turn.
                ExcludeLastUserMessage: false,
                AppendUserMessage: false,
                IsContinuation: isContinuation));
        }, token);
    }

    // -------------------------------------------------------------------------
    // Group conversation
    // -------------------------------------------------------------------------

    public async Task<SceneTurnResult> RunGroupTurnAsync(
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
        if (!_activeGroupTurns.TryAdd(sceneId, 0)) return SceneTurnResult.AlreadyRunning(sceneId);

        var generationId = Guid.NewGuid().ToString("N")[..12];
        try
        {
            var runtime = await BuildGroupRuntimeAsync(sceneId, token);
            var conversation = runtime.Conversation;
            var turn = conversation.TurnState ?? throw new InvalidOperationException("У группового разговора отсутствует состояние хода.");
            if (turn.Status == SceneStatus.Finished) return SceneTurnResult.Finished(sceneId);
            var firstParticipant = conversation.Participants.First(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId == runtime.First.Id);
            var speakerId = conversation.FindParticipant(turn.NextParticipantId)?.CharacterId ?? firstParticipant.CharacterId!.Value;
            var speaker = speakerId == runtime.First.Id ? runtime.First : runtime.Second;
            onStarted?.Invoke(new SceneTurnStarted(sceneId, speakerId, speaker));

            await SetGroupTurnStateAsync(sceneId, SceneStatus.Running, speakerId, scheduleNextTurn: false, token);
            var context = _prompt.BuildGroup(new GroupPromptBuildRequest(conversation, runtime.First, runtime.Second, runtime.Lorebooks, speakerId, contextSize, reservedGenerationTokens, runtime.Personas));
            AppLog.Write($"GEN {generationId} BEGIN mode=scene_turn scene={sceneId} speaker={speakerId}");
            LogPromptBuild(generationId, context);
            var raw = new StringBuilder();
            await foreach (var chunk in generate(context.Messages, token).WithCancellation(token))
            {
                raw.Append(chunk);
                onChunk?.Invoke(raw.ToString());
            }

            var text = normalizeResponse(speaker, raw.ToString()).Trim();
            if (string.IsNullOrWhiteSpace(text)) text = "…";
            var saved = await AppendGroupCharacterMessageAsync(sceneId, speakerId, speaker.Name, speaker.AvatarPath, text, token);
            var nextSpeakerId = speakerId == runtime.First.Id ? runtime.Second.Id : runtime.First.Id;
            var nextStatus = ConversationTurnPolicy.NextStatusAfterGeneratedTurn(turn.Mode);
            await SetGroupTurnStateAsync(sceneId, nextStatus, nextSpeakerId, scheduleNextTurn: true, token);
            return new SceneTurnResult(sceneId, SceneTurnExecutionStatus.Completed, speakerId, speaker.Name, saved, nextSpeakerId, nextStatus, text);
        }
        finally
        {
            _activeGroupTurns.TryRemove(sceneId, out _);
        }
    }

    private Task<GroupConversationRuntime> BuildGroupRuntimeAsync(Guid conversationId, CancellationToken token) =>
        _store.ReadAsync(root =>
        {
            var conversation = (root.Conversations ?? []).FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Group)
                ?? throw new InvalidOperationException("Групповой разговор не найден.");
            var characterIds = conversation.Participants
                .Where(participant => participant.Kind == ConversationParticipantKind.Character && participant.CharacterId is not null)
                .OrderBy(participant => participant.SortOrder)
                .Select(participant => participant.CharacterId!.Value)
                .ToList();
            if (characterIds.Count < 2) throw new InvalidOperationException("Групповой разговор должен содержать двух персонажей.");
            var first = (root.Characters ?? []).FirstOrDefault(character => character.Id == characterIds[0])
                ?? throw new InvalidOperationException("Первый персонаж группового разговора не найден.");
            var second = (root.Characters ?? []).FirstOrDefault(character => character.Id == characterIds[1])
                ?? throw new InvalidOperationException("Второй персонаж группового разговора не найден.");
            return new GroupConversationRuntime(
                conversation,
                first,
                second,
                (root.Lorebooks ?? []).ToDictionary(book => book.Id),
                (root.Personas ?? []).ToDictionary(persona => persona.Id));
        }, token);

    private Task SetGroupTurnStateAsync(Guid conversationId, string status, Guid nextCharacterId, bool scheduleNextTurn, CancellationToken token) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Group)
                ?? throw new InvalidOperationException("Групповой разговор не найден.");
            var turn = conversation.TurnState ?? throw new InvalidOperationException("У группового разговора отсутствует состояние хода.");
            var participant = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId == nextCharacterId)
                ?? throw new InvalidOperationException("Следующий персонаж не является участником разговора.");
            var now = DateTimeOffset.Now;
            turn.Status = status;
            turn.NextParticipantId = participant.Id;
            turn.NextTurnAt = scheduleNextTurn ? ConversationTurnPolicy.NextTurnAt(status, turn.Mode, turn.DelaySeconds, now) : null;
            conversation.UpdatedAt = now;
        }, "set_group_generation_state", token);

    private Task<ConversationMessageSnapshot> AppendGroupCharacterMessageAsync(Guid conversationId, Guid characterId, string authorName, string avatarPath, string content, CancellationToken token) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Group)
                ?? throw new InvalidOperationException("Групповой разговор не найден.");
            var participant = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId == characterId)
                ?? throw new InvalidOperationException("Персонаж не является участником разговора.");
            var now = DateTimeOffset.Now;
            var message = new ConversationMessageSnapshot
            {
                Id = Guid.NewGuid(),
                SequenceNumber = conversation.Messages.Count == 0 ? 1 : conversation.Messages.Max(value => value.SequenceNumber) + 1,
                Kind = ConversationMessageKind.Message,
                AuthorParticipantId = participant.Id,
                AuthorName = authorName,
                AuthorAvatarPath = avatarPath,
                Content = content,
                CreatedAt = now
            };
            conversation.Messages.Add(message);
            conversation.UpdatedAt = now;
            return message;
        }, "append_group_character_message", token);

    private Task<SoulMessage> AppendPersonalCharacterMessageAsync(Guid conversationId, Guid characterId, string authorName, string avatarPath, string content, CancellationToken token) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Personal)
                ?? throw new InvalidOperationException("Личный разговор не найден.");
            var participant = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId == characterId)
                ?? throw new InvalidOperationException("Персонаж не является участником разговора.");
            var now = DateTimeOffset.Now;
            var variant = new ConversationMessageVariantSnapshot(Guid.NewGuid(), "Основной", content, now);
            var message = new ConversationMessageSnapshot
            {
                Id = Guid.NewGuid(),
                SequenceNumber = conversation.Messages.Count == 0 ? 1 : conversation.Messages.Max(value => value.SequenceNumber) + 1,
                Kind = ConversationMessageKind.Message,
                AuthorParticipantId = participant.Id,
                AuthorName = authorName,
                AuthorAvatarPath = avatarPath,
                Content = content,
                CreatedAt = now,
                SelectedVariantId = variant.Id,
                Variants = [variant]
            };
            conversation.Messages.Add(message);
            conversation.UpdatedAt = now;
            return ConversationMessageMapper.ToPersonalMessage(conversation, message);
        }, "append_personal_character_message", token);

    private static void LogPromptBuild(string generationId, PromptBuildResult context)
    {
        var promptText = string.Join("\n", context.Messages.Select(message => $"{message.role}:{message.content}"));
        var diagnosticSnapshot = PromptDiagnosticSnapshotStore.Publish(generationId, context);
        AppLog.Write($"GEN {generationId} PROMPT messages={context.Messages.Count} chars={promptText.Length} hash={AppLog.Fingerprint(promptText)} {diagnosticSnapshot.Trace}");
    }
}

public enum DirectTurnExecutionStatus { Completed, AlreadyRunning }

public sealed record DirectTurnResult(
    Guid CharacterId,
    Guid ChatId,
    DirectTurnExecutionStatus Status,
    string CharacterName,
    SoulMessage? SavedMessage,
    string Content,
    string RawContent)
{
    public static DirectTurnResult AlreadyRunning(Guid characterId, Guid chatId) =>
        new(characterId, chatId, DirectTurnExecutionStatus.AlreadyRunning, "", null, "", "");
}

public sealed record SceneTurnStarted(Guid SceneId, Guid SpeakerCharacterId, SoulCharacter Speaker);
public enum SceneTurnExecutionStatus { Completed, AlreadyRunning, Finished }
public sealed record SceneTurnResult(
    Guid SceneId,
    SceneTurnExecutionStatus Status,
    Guid? SpeakerCharacterId,
    string SpeakerName,
    ConversationMessageSnapshot? SavedMessage,
    Guid? NextSpeakerCharacterId,
    string NextStatus,
    string Content)
{
    public static SceneTurnResult AlreadyRunning(Guid sceneId) => new(sceneId, SceneTurnExecutionStatus.AlreadyRunning, null, "", null, null, "", "");
    public static SceneTurnResult Finished(Guid sceneId) => new(sceneId, SceneTurnExecutionStatus.Finished, null, "", null, null, SceneStatus.Finished, "");
}
