using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Compatibility read layer over the current persisted model. It exposes chats and scenes as one
/// collection without moving, rewriting or deleting any data. A later migration can use this as
/// an oracle to prove that the new Conversation store preserved every user-visible record.
/// </summary>
public sealed class ConversationReadService
{
    private static readonly Guid DirectUserParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid DirectorParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid SystemParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    public IReadOnlyList<ConversationSnapshot> ReadAll(SoulDataRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var conversations = new List<ConversationSnapshot>();
        foreach (var character in root.Characters ?? [])
            foreach (var chat in character.Chats ?? [])
                conversations.Add(ReadChat(character, chat));
        foreach (var scene in root.Scenes ?? [])
            conversations.Add(ReadScene(root, scene));
        return conversations.OrderByDescending(conversation => conversation.UpdatedAt).ToList();
    }

    public ConversationSnapshot ReadChat(SoulCharacter character, SoulChat chat)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(chat);
        var characterParticipant = CharacterParticipant(character, 1);
        var participants = new[]
        {
            new ConversationParticipant(DirectUserParticipantId, ConversationParticipantKind.User, "Вы", null, false, 0),
            characterParticipant
        };
        var messages = (chat.Messages ?? []).OrderBy(message => message.SequenceNumber)
            .Select(message => ToChatMessage(message, characterParticipant.Id)).ToList();
        return new ConversationSnapshot
        {
            Id = chat.Id,
            Kind = ConversationKind.Direct,
            Source = ConversationSource.CharacterChat,
            Name = chat.Name,
            IsPinned = chat.IsPinned,
            IsArchived = chat.IsArchived,
            SummaryText = chat.SummaryText,
            LastSummarizedSequence = chat.LastSummarizedSequence,
            CreatedAt = chat.CreatedAt,
            UpdatedAt = chat.UpdatedAt,
            Participants = participants,
            Messages = messages,
            Context = new ConversationContextSnapshot
            {
                InitialUserProfile = chat.InitialUserProfile,
                InitialRelationshipContext = chat.InitialRelationshipContext,
                Memory = chat.Memory,
                StateValues = new Dictionary<Guid, string>(chat.StateValuesJson ?? [])
            }
        };
    }

    public ConversationSnapshot ReadScene(SoulDataRoot root, SoulScene scene)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scene);
        var first = root.Characters.FirstOrDefault(character => character.Id == scene.CharacterAId)
            ?? throw new InvalidOperationException("Первый персонаж сцены не найден.");
        var second = root.Characters.FirstOrDefault(character => character.Id == scene.CharacterBId)
            ?? throw new InvalidOperationException("Второй персонаж сцены не найден.");
        var firstParticipant = CharacterParticipant(first, 0);
        var secondParticipant = CharacterParticipant(second, 1);
        var participants = new[]
        {
            firstParticipant,
            secondParticipant,
            new ConversationParticipant(DirectorParticipantId, ConversationParticipantKind.Director, "Режиссёр", null, false, 2)
        };
        var messages = (scene.Messages ?? []).OrderBy(message => message.SequenceNumber)
            .Select(message => ToSceneMessage(message, firstParticipant, secondParticipant)).ToList();
        return new ConversationSnapshot
        {
            Id = scene.Id,
            Kind = ConversationKind.Scene,
            Source = ConversationSource.RootScene,
            Name = scene.Name,
            IsPinned = scene.IsPinned,
            SummaryText = scene.SummaryText,
            LastSummarizedSequence = scene.LastSummarizedSequence,
            CreatedAt = scene.CreatedAt,
            UpdatedAt = scene.UpdatedAt,
            Participants = participants,
            Messages = messages,
            Context = new ConversationContextSnapshot
            {
                Scenario = scene.Scenario,
                Location = scene.Location,
                TimeContext = scene.TimeContext,
                Mood = scene.Mood,
                Goal = scene.Goal,
                RelationshipContext = scene.RelationshipContext
            },
            TurnState = new ConversationTurnState(
                scene.Status,
                scene.TurnMode,
                scene.NextCharacterId == first.Id ? firstParticipant.Id : scene.NextCharacterId == second.Id ? secondParticipant.Id : null,
                scene.NextTurnAt,
                scene.DelaySeconds,
                scene.EnforceSceneContract,
                scene.AdvanceSceneAndAvoidRepetition)
        };
    }

    private static ConversationParticipant CharacterParticipant(SoulCharacter character, int sortOrder) =>
        new(character.Id, ConversationParticipantKind.Character, character.Name, character.Id, true, sortOrder);

    private static ConversationMessageSnapshot ToChatMessage(SoulMessage message, Guid characterParticipantId)
    {
        var author = message.Role switch
        {
            SoulMessageRole.User => DirectUserParticipantId,
            SoulMessageRole.Assistant => characterParticipantId,
            _ => SystemParticipantId
        };
        var variants = (message.Variants ?? []).Select(variant => new ConversationMessageVariantSnapshot(variant.Id, variant.Label, variant.Content, variant.CreatedAt)).ToList();
        var selected = variants.FirstOrDefault(variant => variant.Id == message.CurrentVariantId) ?? variants.FirstOrDefault();
        var attachments = (message.Attachments ?? []).Select(attachment => new ConversationAttachmentSnapshot(attachment.Id, attachment.MediaType, attachment.LocalPath, attachment.OriginalName, attachment.CreatedAt)).ToList();
        return new ConversationMessageSnapshot
        {
            Id = message.Id,
            SequenceNumber = message.SequenceNumber,
            Kind = message.Role == SoulMessageRole.System ? ConversationMessageKind.SystemEvent : ConversationMessageKind.Message,
            AuthorParticipantId = author,
            AuthorName = message.AuthorName,
            Content = selected?.Content ?? "",
            CreatedAt = message.CreatedAt,
            EditedAt = message.EditedAt,
            Variants = variants,
            Attachments = attachments
        };
    }

    private static ConversationMessageSnapshot ToSceneMessage(
        SoulSceneMessage message,
        ConversationParticipant first,
        ConversationParticipant second)
    {
        var director = message.Kind == SoulSceneMessageKind.Director;
        Guid? author = director
            ? DirectorParticipantId
            : message.SpeakerCharacterId == first.CharacterId ? first.Id
            : message.SpeakerCharacterId == second.CharacterId ? second.Id
            : null;
        return new ConversationMessageSnapshot
        {
            Id = message.Id,
            SequenceNumber = message.SequenceNumber,
            Kind = director ? ConversationMessageKind.DirectorEvent : ConversationMessageKind.Message,
            AuthorParticipantId = author,
            AuthorName = message.SpeakerName,
            Content = message.Content,
            CreatedAt = message.CreatedAt
        };
    }
}
