using System.Linq;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Canonical read/write service for personal and group conversations.
/// </summary>
public sealed class ConversationService
{
    private static readonly Guid UserParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirectorParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private readonly JsonDataStore _store;
    private readonly CharacterLibraryService _characters;

    public ConversationService(JsonDataStore store, CharacterLibraryService characters)
    {
        _store = store;
        _characters = characters;
    }

    public static ConversationActionCapabilities CapabilitiesFor(ConversationKind kind) => ConversationCapabilityPolicy.For(kind);

    public Task<ConversationMutationResult> GetAsync(ConversationAddress address, CancellationToken token = default) =>
        ReadResultAsync(address, token);

    public Task<IReadOnlyList<ConversationSnapshot>> GetAllAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<ConversationSnapshot>)(root.Conversations ?? []).OrderByDescending(value => value.UpdatedAt).ToList(), token);

    public Task<IReadOnlyList<ConversationSnapshot>> GetGroupsAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<ConversationSnapshot>)(root.Conversations ?? []).Where(value => value.Mode == ConversationMode.Group).OrderByDescending(value => value.UpdatedAt).ToList(), token);

    public Task<ConversationSnapshot?> GetGroupAsync(Guid conversationId, CancellationToken token = default) =>
        _store.ReadAsync(root => (root.Conversations ?? []).FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Group), token);

    public Task UpdatePersonalContextAsync(Guid conversationId, string? userProfile, string? relationshipContext, CancellationToken token = default) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Personal)
                ?? throw new InvalidOperationException("Личный разговор не найден.");
            conversation.Context.InitialUserProfile = userProfile?.Trim() ?? "";
            conversation.Context.InitialRelationshipContext = relationshipContext?.Trim() ?? "";
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "update_personal_context", token);

    public Task SelectPersonalAsync(Guid characterId, Guid conversationId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = (root.Characters ?? []).FirstOrDefault(value => value.Id == characterId)
                ?? throw new InvalidOperationException("Персонаж не найден.");
            if (!(root.Conversations ?? []).Any(value => value.Id == conversationId && value.Mode == ConversationMode.Personal &&
                    value.Participants.Any(participant => participant.CharacterId == characterId)))
                throw new InvalidOperationException("Личный разговор персонажа не найден.");
            character.CurrentChatId = conversationId;
            character.UpdatedAt = DateTimeOffset.Now;
        }, "select_personal_conversation", token);

    public Task<ConversationSnapshot> EditPersonalMessageAsync(Guid conversationId, Guid messageId, string content, CancellationToken token = default) =>
        MutatePersonalAsync(conversationId, "edit_personal_message", conversation =>
        {
            var message = RequireMessage(conversation, messageId);
            var variant = message.Variants.FirstOrDefault(value => value.Id == message.SelectedVariantId)
                ?? message.Variants.FirstOrDefault()
                ?? throw new InvalidOperationException("Текущий вариант сообщения не найден.");
            variant = variant with { Content = content };
            var index = message.Variants.FindIndex(value => value.Id == variant.Id);
            message.Variants[index] = variant;
            message.Content = content;
            message.EditedAt = DateTimeOffset.Now;
            ResetPersonalCognition(conversation);
        }, token);

    public Task<ConversationSnapshot> DeletePersonalMessageAsync(Guid conversationId, Guid messageId, CancellationToken token = default) =>
        MutatePersonalAsync(conversationId, "delete_personal_message", conversation =>
        {
            if (conversation.Messages.RemoveAll(value => value.Id == messageId) == 0)
                throw new InvalidOperationException("Сообщение не найдено.");
            RenumberMessages(conversation);
            ResetPersonalCognition(conversation);
        }, token);

    public Task<ConversationSnapshot> SelectPersonalVariantAsync(Guid conversationId, Guid messageId, Guid variantId, CancellationToken token = default) =>
        MutatePersonalAsync(conversationId, "select_personal_variant", conversation =>
        {
            var message = RequireMessage(conversation, messageId);
            var variant = message.Variants.FirstOrDefault(value => value.Id == variantId)
                ?? throw new InvalidOperationException("Вариант ответа не найден.");
            message.SelectedVariantId = variant.Id;
            message.Content = variant.Content;
            message.EditedAt = DateTimeOffset.Now;
        }, token);

    public Task<(int Removed, ConversationSnapshot Conversation)> TruncatePersonalAfterAsync(Guid conversationId, Guid messageId, CancellationToken token = default) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = RequirePersonal(conversations, conversationId);
            var pivot = RequireMessage(conversation, messageId);
            var removed = conversation.Messages.RemoveAll(value => value.SequenceNumber > pivot.SequenceNumber);
            if (removed > 0) ResetPersonalCognition(conversation);
            RenumberMessages(conversation);
            conversation.UpdatedAt = DateTimeOffset.Now;
            return (removed, conversation);
        }, "truncate_personal_branch", token);

    public async Task<SceneSummaryResult> UpdateGroupSummaryAsync(Guid conversationId, Func<IReadOnlyList<LlamaMessage>, CancellationToken, Task<string>> complete, bool force = false, int intervalMessages = 6, CancellationToken token = default)
    {
        var interval = Math.Clamp(intervalMessages, 2, 20);
        var input = await _store.ReadAsync(root =>
        {
            var conversation = (root.Conversations ?? []).FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Group)
                ?? throw new InvalidOperationException("Групповой разговор не найден.");
            var pending = conversation.Messages.Where(message => message.SequenceNumber > conversation.LastSummarizedSequence).OrderBy(message => message.SequenceNumber).ToList();
            return new GroupSummaryInput(conversation.Name, conversation.Context.Scenario, conversation.Context.RelationshipContext, CompactSummary(conversation.SummaryText), pending.Take(interval).ToList(), pending.Count);
        }, token);
        if (!force && input.PendingCount < interval) return new SceneSummaryResult(false, "Summary группового разговора пока не требует обновления.");
        if (input.Messages.Count == 0) return new SceneSummaryResult(false, "В разговоре нет новых реплик для Summary.");
        const string system = """
            You maintain a compact shared summary for a multi-character roleplay conversation. Return plain text only.
            Preserve confirmed facts; never invent dialogue, motives or events. Use exact headings:
            [SCENE STATE]
            [RELATIONSHIP DYNAMICS]
            [CURRENT SITUATION]
            [KEY EVENTS]
            Use exactly the 4 headings above. Under each heading write at most 2 short bullet points.
            Maximum 180 words and 1,200 characters total. Prefer current state and facts required for upcoming turns.
            """;
        var dialogue = string.Join("\n", input.Messages.Select(message => $"{message.AuthorName}: {message.Content}"));
        var user = $"CONVERSATION: {input.Name}\nSCENARIO: {input.Scenario}\nSHARED RELATIONSHIP: {input.RelationshipContext}\n\nEXISTING COMPACT SUMMARY:\n{input.ExistingSummary}\n\nNEW EVENTS:\n{dialogue}";
        var summary = CompactSummary((await complete([new LlamaMessage("system", system), new LlamaMessage("user", user)], token)).Trim());
        if (summary.Length < 20) return new SceneSummaryResult(false, "Summary не обновлён: ответ модели слишком короткий.");
        await _store.MutateConversationsAsync(values =>
        {
            var conversation = GetCanonical(values, conversationId);
            conversation.SummaryText = summary;
            conversation.LastSummarizedSequence = input.Messages.Max(message => message.SequenceNumber);
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "update_group_summary", token);
        return new SceneSummaryResult(true, "Summary группового разговора обновлён.");
    }

    public async Task<ConversationMutationResult> CreateAsync(
        IReadOnlyList<Guid> characterIds,
        string name,
        string scenario = "",
        string location = "",
        string timeContext = "",
        string mood = "",
        string goal = "",
        string relationshipContext = "",
        string turnMode = "alternate",
        int delaySeconds = 0,
        bool enforceContract = true,
        bool advanceAndAvoidRepetition = true,
        CancellationToken token = default)
    {
        var ids = characterIds.Distinct().ToList();
        if (ids.Count is < 1 or > 2) throw new InvalidOperationException("Выберите одного или двух разных персонажей.");
        var characters = await _store.ReadAsync(root => ids.Select(id =>
            (root.Characters ?? []).FirstOrDefault(value => value.Id == id)
            ?? throw new InvalidOperationException("Персонаж не найден.")).ToList(), token);
        var now = DateTimeOffset.Now;
        var conversation = new ConversationSnapshot
        {
            Id = Guid.NewGuid(),
            Kind = ids.Count == 1 ? ConversationKind.Direct : ConversationKind.Scene,
            Source = ids.Count == 1 ? ConversationSource.CharacterChat : ConversationSource.RootScene,
            Name = string.IsNullOrWhiteSpace(name) ? (ids.Count == 1 ? "Новый разговор" : string.Join(" и ", characters.Select(value => value.Name))) : name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Participants = ids.Count == 1
                ? [new(UserParticipantId, ConversationParticipantKind.User, "Вы", null, false, 0), new(characters[0].Id, ConversationParticipantKind.Character, characters[0].Name, characters[0].Id, true, 1)]
                : [new(characters[0].Id, ConversationParticipantKind.Character, characters[0].Name, characters[0].Id, true, 0), new(characters[1].Id, ConversationParticipantKind.Character, characters[1].Name, characters[1].Id, true, 1), new(UserParticipantId, ConversationParticipantKind.User, "Вы", null, false, 2), new(DirectorParticipantId, ConversationParticipantKind.Director, "Режиссёр", null, false, 3)],
            Context = new ConversationContextSnapshot
            {
                InitialUserProfile = ids.Count == 1 ? characters[0].DefaultUserProfile : "",
                InitialRelationshipContext = ids.Count == 1 ? characters[0].DefaultRelationshipContext : "",
                Scenario = scenario.Trim(), Location = location.Trim(), TimeContext = timeContext.Trim(), Mood = mood.Trim(), Goal = goal.Trim(), RelationshipContext = relationshipContext.Trim(),
                Memory = ids.Count == 1 ? new SoulMemoryBundle() : null
            },
            TurnState = ids.Count == 2 ? new ConversationTurnState(SceneStatus.Paused, turnMode == "manual" ? "manual" : "alternate", characters[0].Id, null, Math.Clamp(delaySeconds, 0, 30), enforceContract, advanceAndAvoidRepetition) : null
        };
        await _store.MutateConversationsAsync(values => values.Add(conversation), "create_conversation", token);
        return await ReadResultAsync(new ConversationAddress(conversation.Id, conversation.Kind), token);
    }

    public async Task<ConversationMutationResult> UpdateGroupAsync(Guid conversationId, IReadOnlyList<Guid> characterIds, string name, string scenario, string location, string timeContext, string mood, string goal, string relationshipContext, string turnMode, int delaySeconds, bool enforceContract, bool advanceAndAvoidRepetition, CancellationToken token = default)
    {
        var ids = characterIds.Distinct().ToList();
        if (ids.Count != 2) throw new InvalidOperationException("Выберите двух разных персонажей.");
        var characters = await _store.ReadAsync(root => ids.Select(id => (root.Characters ?? []).FirstOrDefault(value => value.Id == id) ?? throw new InvalidOperationException("Персонаж не найден.")).ToList(), token);
        await _store.MutateConversationsAsync(values =>
        {
            var conversation = GetCanonical(values, conversationId);
            if (conversation.Mode != ConversationMode.Group) throw new InvalidOperationException("Ожидался групповой разговор.");
            conversation.Name = string.IsNullOrWhiteSpace(name) ? conversation.Name : name.Trim();
            conversation.Participants = [
                new(characters[0].Id, ConversationParticipantKind.Character, characters[0].Name, characters[0].Id, true, 0),
                new(characters[1].Id, ConversationParticipantKind.Character, characters[1].Name, characters[1].Id, true, 1),
                new(UserParticipantId, ConversationParticipantKind.User, "Вы", null, false, 2),
                new(DirectorParticipantId, ConversationParticipantKind.Director, "Режиссёр", null, false, 3)];
            conversation.Context.Scenario = scenario.Trim(); conversation.Context.Location = location.Trim(); conversation.Context.TimeContext = timeContext.Trim(); conversation.Context.Mood = mood.Trim(); conversation.Context.Goal = goal.Trim(); conversation.Context.RelationshipContext = relationshipContext.Trim();
            var turn = conversation.TurnState ??= new ConversationTurnState(SceneStatus.Paused, "alternate", characters[0].Id, null, 0, true, true);
            turn.Mode = turnMode == "manual" ? "manual" : "alternate"; turn.DelaySeconds = Math.Clamp(delaySeconds, 0, 30); turn.EnforceContract = enforceContract; turn.AdvanceAndAvoidRepetition = advanceAndAvoidRepetition;
            if (conversation.FindParticipant(turn.NextParticipantId)?.CharacterId is not { } next || !ids.Contains(next)) turn.NextParticipantId = characters[0].Id;
            turn.NextTurnAt = ConversationTurnPolicy.NextTurnAt(turn.Status, turn.Mode, turn.DelaySeconds, DateTimeOffset.Now);
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "update_group_conversation", token);
        return await ReadResultAsync(ConversationAddress.Scene(conversationId), token);
    }

    public Task<ConversationAddress> ResolveAddressAsync(Guid conversationId, CancellationToken token = default) =>
        _store.ReadAsync(root =>
        {
            var persisted = root.Conversations?.FirstOrDefault(value => value.Id == conversationId);
            if (persisted is not null)
                return new ConversationAddress(conversationId, persisted.Source == ConversationSource.RootScene ? ConversationKind.Scene : ConversationKind.Direct);
            throw new InvalidOperationException("Разговор не найден.");
        }, token);

    public async Task<DirectConversationTarget> ResolveDirectAsync(Guid chatId, CancellationToken token = default)
    {
        return await _store.ReadAsync(root =>
        {
            var conversation = root.Conversations.FirstOrDefault(value => value.Id == chatId && value.Mode == ConversationMode.Personal)
                ?? throw new InvalidOperationException("Личный разговор не найден.");
            var characterId = conversation.Participants.Where(value => value.Kind == ConversationParticipantKind.Character)
                .OrderBy(value => value.SortOrder).Select(value => value.CharacterId).FirstOrDefault()
                ?? throw new InvalidOperationException("В личном разговоре отсутствует персонаж.");
            var character = root.Characters.FirstOrDefault(value => value.Id == characterId)
                ?? throw new InvalidOperationException("Персонаж не найден.");
            return new DirectConversationTarget(character.Id, conversation.Id, character.Name, conversation.Name);
        }, token);
    }

    public async Task<ConversationMutationResult> AppendUserMessageAsync(ConversationAddress address, string content, CancellationToken token = default)
        => await AppendAuthoredUserMessageAsync(address, content, SoulMessageAuthorKind.User, null, "Вы", "", token);

    public async Task<ConversationMutationResult> AppendAuthoredUserMessageAsync(ConversationAddress address, string content, SoulMessageAuthorKind authorKind, Guid? personaId, string authorName, string avatarPath, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Введите текст сообщения.");
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            var user = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.User);
            AppendCanonicalMessage(conversation, authorKind == SoulMessageAuthorKind.Director ? ConversationMessageKind.DirectorEvent : ConversationMessageKind.Message,
                authorKind == SoulMessageAuthorKind.Director ? conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Director)?.Id : user?.Id,
                authorKind, personaId, authorName, avatarPath, content);
        }, "append_user_message", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> AppendAssistantMessageAsync(ConversationAddress address, string content, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Пустой ответ модели.");
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            if (conversation.Mode != ConversationMode.Personal) throw new InvalidOperationException("Ожидался личный разговор.");
            var character = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Character)
                ?? throw new InvalidOperationException("Персонаж разговора не найден.");
            AppendCanonicalMessage(conversation, ConversationMessageKind.Message, character.Id, SoulMessageAuthorKind.User, null, character.DisplayName, "", content);
        }, "append_character_message", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> AddDirectorEventAsync(ConversationAddress address, string content, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Введите режиссёрское событие.");
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            var director = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Director);
            AppendCanonicalMessage(conversation, ConversationMessageKind.DirectorEvent, director?.Id, SoulMessageAuthorKind.Director, null, "Режиссёр", "", content);
        }, "append_director_event", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> AddSceneUserMessageAsync(ConversationAddress address, string content, Guid? personaId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("Введите текст сообщения.");
        var persona = personaId is null ? null : await _store.ReadAsync(root =>
            (root.Personas ?? []).FirstOrDefault(value => value.Id == personaId.Value), token)
            ?? throw new InvalidOperationException("Персона не найдена.");
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            if (conversation.Mode != ConversationMode.Group) throw new InvalidOperationException("Это действие доступно только для группового разговора.");
            var user = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.User);
            AppendCanonicalMessage(conversation, ConversationMessageKind.Message, user?.Id,
                personaId is null ? SoulMessageAuthorKind.User : SoulMessageAuthorKind.Persona,
                personaId, persona?.Name ?? "Вы", persona?.AvatarPath ?? "", content);
        }, "append_group_user_message", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> SetSceneStatusAsync(ConversationAddress address, ConversationSceneStatusAction action, CancellationToken token = default)
    {
        var status = action switch
        {
            ConversationSceneStatusAction.Start => SceneStatus.Running,
            ConversationSceneStatusAction.Pause => SceneStatus.Paused,
            ConversationSceneStatusAction.Finish => SceneStatus.Finished,
            _ => throw new InvalidOperationException("Неизвестное действие сцены.")
        };
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            if (conversation.Mode != ConversationMode.Group || conversation.TurnState is null)
                throw new InvalidOperationException("Это действие доступно только для группового разговора.");
            conversation.TurnState.Status = status;
            conversation.TurnState.NextTurnAt = ConversationTurnPolicy.NextTurnAt(status, conversation.TurnState.Mode, conversation.TurnState.DelaySeconds, DateTimeOffset.Now);
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "set_group_status", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> ChooseSceneNextParticipantAsync(ConversationAddress address, Guid characterId, CancellationToken token = default)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            if (conversation.Mode != ConversationMode.Group || conversation.TurnState is null)
                throw new InvalidOperationException("Это действие доступно только для группового разговора.");
            var participant = conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId == characterId)
                ?? throw new InvalidOperationException("Участник не входит в групповой разговор.");
            conversation.TurnState.Status = SceneStatus.Paused;
            conversation.TurnState.NextParticipantId = participant.Id;
            conversation.TurnState.NextTurnAt = null;
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "choose_group_participant", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> SetPinnedAsync(ConversationAddress address, bool pinned, CancellationToken token = default)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            conversation.IsPinned = pinned;
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, pinned ? "pin_conversation" : "unpin_conversation", token);
        return await ReadResultAsync(address, token);
    }

    public async Task<ConversationMutationResult> RenameAsync(ConversationAddress address, string name, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Введите название.");
        var trimmed = name.Trim();

        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = GetCanonical(conversations, address.Id);
            conversation.Name = trimmed;
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "rename_conversation", token);
        return await ReadResultAsync(address, token);
    }

    public async Task DeleteAsync(ConversationAddress address, CancellationToken token = default)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            if (conversations.RemoveAll(value => value.Id == address.Id) == 0)
                throw new InvalidOperationException("Разговор не найден.");
        }, "delete_conversation", token);
    }

    private async Task<ConversationMutationResult> ReadResultAsync(ConversationAddress address, CancellationToken token)
    {
        var conversation = await _store.ReadAsync(root =>
        {
            return root.Conversations?.FirstOrDefault(value => value.Id == address.Id)
                ?? throw new InvalidOperationException("Разговор не найден.");
        }, token);
        return new ConversationMutationResult(conversation, ConversationCapabilityPolicy.For(conversation));
    }

    private static void RequireScene(ConversationAddress address)
    {
        if (address.Kind != ConversationKind.Scene)
            throw new InvalidOperationException("Это действие доступно только для сцены.");
    }

    private static ConversationSnapshot GetCanonical(List<ConversationSnapshot> conversations, Guid conversationId) =>
        conversations.FirstOrDefault(value => value.Id == conversationId)
        ?? throw new InvalidOperationException("Разговор не найден.");

    private Task<ConversationSnapshot> MutatePersonalAsync(Guid conversationId, string reason, Action<ConversationSnapshot> mutation, CancellationToken token) =>
        _store.MutateConversationsAsync(conversations =>
        {
            var conversation = RequirePersonal(conversations, conversationId);
            mutation(conversation);
            conversation.UpdatedAt = DateTimeOffset.Now;
            return conversation;
        }, reason, token);

    private static ConversationSnapshot RequirePersonal(List<ConversationSnapshot> conversations, Guid conversationId) =>
        conversations.FirstOrDefault(value => value.Id == conversationId && value.Mode == ConversationMode.Personal)
        ?? throw new InvalidOperationException("Личный разговор не найден.");

    private static ConversationMessageSnapshot RequireMessage(ConversationSnapshot conversation, Guid messageId) =>
        conversation.Messages.FirstOrDefault(value => value.Id == messageId)
        ?? throw new InvalidOperationException("Сообщение не найдено.");

    private static void ResetPersonalCognition(ConversationSnapshot conversation)
    {
        conversation.SummaryText = "";
        conversation.LastSummarizedSequence = 0;
        conversation.Context.Memory = new SoulMemoryBundle();
    }

    private static void RenumberMessages(ConversationSnapshot conversation)
    {
        var sequence = 1;
        foreach (var message in conversation.Messages.OrderBy(value => value.SequenceNumber))
            message.SequenceNumber = sequence++;
    }

    private static void AppendCanonicalMessage(
        ConversationSnapshot conversation,
        ConversationMessageKind kind,
        Guid? participantId,
        SoulMessageAuthorKind authorKind,
        Guid? personaId,
        string authorName,
        string avatarPath,
        string content)
    {
        var now = DateTimeOffset.Now;
        var text = content.Trim();
        var variant = new ConversationMessageVariantSnapshot(Guid.NewGuid(), "Основной", text, now);
        conversation.Messages.Add(new ConversationMessageSnapshot
        {
            Id = Guid.NewGuid(),
            SequenceNumber = conversation.Messages.Count == 0 ? 1 : conversation.Messages.Max(value => value.SequenceNumber) + 1,
            Kind = kind,
            AuthorParticipantId = participantId,
            AuthorKind = authorKind,
            AuthorPersonaId = personaId,
            AuthorName = authorName,
            AuthorAvatarPath = avatarPath,
            Content = text,
            CreatedAt = now,
            SelectedVariantId = variant.Id,
            Variants = [variant]
        });
        conversation.UpdatedAt = now;
    }

    private static string CompactSummary(string? text)
    {
        var clean = (text ?? "").Trim();
        const int maxCharacters = 1200;
        if (clean.Length <= maxCharacters) return clean;
        var cut = clean.LastIndexOf(' ', maxCharacters - 1);
        if (cut < maxCharacters / 2) cut = maxCharacters;
        return clean[..cut].TrimEnd() + "…";
    }

    private sealed record GroupSummaryInput(string Name, string Scenario, string RelationshipContext, string ExistingSummary, IReadOnlyList<ConversationMessageSnapshot> Messages, int PendingCount);
}
