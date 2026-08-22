using System.Linq;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Compatibility write facade for the common Conversation vocabulary. It delegates persistence to
/// the proven CharacterLibraryService and SceneService, so it does not migrate or rewrite legacy
/// JSON. Generation remains owned by ConversationTurnRunner because it needs a model transport.
/// </summary>
public sealed class ConversationService
{
    private readonly JsonDataStore _store;
    private readonly CharacterLibraryService _characters;
    private readonly SceneService _scenes;
    private readonly ConversationReadService _reader;

    public ConversationService(JsonDataStore store, CharacterLibraryService characters, SceneService scenes, ConversationReadService? reader = null)
    {
        _store = store;
        _characters = characters;
        _scenes = scenes;
        _reader = reader ?? new ConversationReadService();
    }

    public static ConversationActionCapabilities CapabilitiesFor(ConversationKind kind) => ConversationCapabilityPolicy.For(kind);

    public async Task<ConversationMutationResult> AppendUserMessageAsync(ConversationAddress address, string content, CancellationToken token = default)
    {
        if (address.Kind != ConversationKind.Direct) throw new InvalidOperationException("Пользовательская реплика этого типа разговора пока не поддерживается общим сервисом.");
        var target = await FindDirectTargetAsync(address.Id, token);
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Введите текст сообщения.");
        await _characters.AddMessageAsync(target.CharacterId, target.ChatId, SoulMessageRole.User, "Вы", content.Trim(), token: token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> AddDirectorEventAsync(ConversationAddress address, string content, CancellationToken token = default)
    {
        RequireScene(address);
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Введите режиссёрское событие.");
        await _scenes.AddDirectorMessageAsync(address.Id, content.Trim(), token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> SetSceneStatusAsync(ConversationAddress address, ConversationSceneStatusAction action, CancellationToken token = default)
    {
        RequireScene(address);
        var status = action switch
        {
            ConversationSceneStatusAction.Start => "running",
            ConversationSceneStatusAction.Pause => "paused",
            ConversationSceneStatusAction.Finish => "finished",
            _ => throw new InvalidOperationException("Неизвестное действие сцены.")
        };
        await _scenes.SetStatusAsync(address.Id, status, token: token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> ChooseSceneNextParticipantAsync(ConversationAddress address, Guid characterId, CancellationToken token = default)
    {
        RequireScene(address);
        var scene = await _scenes.GetSceneAsync(address.Id, token) ?? throw new InvalidOperationException("Сцена не найдена.");
        if (characterId != scene.CharacterAId && characterId != scene.CharacterBId) throw new InvalidOperationException("Участник не входит в эту сцену.");
        await _scenes.SetStatusAsync(address.Id, "paused", characterId, token);
        return await ReadResultAsync(address, token);
    }

    public Task<ConversationMutationResult> GetAsync(ConversationAddress address, CancellationToken token = default) => ReadResultAsync(address, token);

    private async Task<ConversationMutationResult> ReadResultAsync(ConversationAddress address, CancellationToken token)
    {
        var conversation = await _store.ReadAsync(root =>
        {
            if (address.Kind == ConversationKind.Scene)
            {
                var scene = root.Scenes.FirstOrDefault(value => value.Id == address.Id) ?? throw new InvalidOperationException("Сцена не найдена.");
                return _reader.ReadScene(root, scene);
            }

            var target = FindDirectTarget(root, address.Id);
            return _reader.ReadChat(target.Character, target.Chat);
        }, token);
        return new ConversationMutationResult(conversation, CapabilitiesFor(conversation.Kind));
    }

    private async Task<(Guid CharacterId, Guid ChatId)> FindDirectTargetAsync(Guid chatId, CancellationToken token)
    {
        var target = await _store.ReadAsync(root => FindDirectTarget(root, chatId), token);
        return (target.Character.Id, target.Chat.Id);
    }

    private static (SoulCharacter Character, SoulChat Chat) FindDirectTarget(SoulDataRoot root, Guid chatId)
    {
        foreach (var character in root.Characters)
        {
            var chat = character.Chats.FirstOrDefault(value => value.Id == chatId);
            if (chat is not null) return (character, chat);
        }
        throw new InvalidOperationException("Чат не найден.");
    }

    private static void RequireScene(ConversationAddress address)
    {
        if (address.Kind != ConversationKind.Scene) throw new InvalidOperationException("Это действие доступно только для сцены.");
    }
}
