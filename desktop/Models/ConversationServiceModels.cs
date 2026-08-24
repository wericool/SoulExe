namespace SoulExe.Models;

/// <summary>Identifies an adapted conversation without introducing a persisted Conversations collection.</summary>
public sealed record ConversationAddress(Guid Id, ConversationKind Kind)
{
    public static ConversationAddress Direct(Guid chatId) => new(chatId, ConversationKind.Direct);
    public static ConversationAddress Scene(Guid sceneId) => new(sceneId, ConversationKind.Scene);
}

/// <summary>Explicit, transport-neutral actions a client may present for a conversation.</summary>
public sealed record ConversationActionCapabilities(
    bool CanAppendUserMessage,
    bool CanAddDirectorEvent,
    bool CanStart,
    bool CanPause,
    bool CanFinish,
    bool CanChooseNextParticipant,
    bool CanGenerateNextTurn,
    bool CanPin = true,
    bool CanRename = true,
    bool CanDelete = true);

public static class ConversationCapabilityPolicy
{
    public static ConversationActionCapabilities For(ConversationSnapshot conversation) => For(conversation.Mode);

    public static ConversationActionCapabilities For(ConversationMode mode) => mode switch
    {
        ConversationMode.Personal => new(
            CanAppendUserMessage: true,
            CanAddDirectorEvent: true,
            CanStart: false,
            CanPause: false,
            CanFinish: false,
            CanChooseNextParticipant: false,
            CanGenerateNextTurn: true,
            CanPin: true,
            CanRename: true,
            CanDelete: true),
        ConversationMode.Group => new(
            CanAppendUserMessage: true,
            CanAddDirectorEvent: true,
            CanStart: true,
            CanPause: true,
            CanFinish: true,
            CanChooseNextParticipant: true,
            CanGenerateNextTurn: true,
            CanPin: true,
            CanRename: true,
            CanDelete: true),
        _ => new(false, false, false, false, false, false, false, false, false, false)
    };

    // Compatibility overload for callers that still address the schema-v9 legacy storage.
    public static ConversationActionCapabilities For(ConversationKind kind) => kind switch
    {
        ConversationKind.Direct => new(
            CanAppendUserMessage: true,
            CanAddDirectorEvent: true,
            CanStart: false,
            CanPause: false,
            CanFinish: false,
            CanChooseNextParticipant: false,
            CanGenerateNextTurn: true,
            CanPin: true,
            CanRename: true,
            CanDelete: true),
        ConversationKind.Scene => new(
            CanAppendUserMessage: true,
            CanAddDirectorEvent: true,
            CanStart: true,
            CanPause: true,
            CanFinish: true,
            CanChooseNextParticipant: true,
            CanGenerateNextTurn: true,
            CanPin: true,
            CanRename: true,
            CanDelete: true),
        _ => new(false, false, false, false, false, false, false, false, false, false)
    };
}

public enum ConversationSceneStatusAction { Start, Pause, Finish }

public sealed record ConversationMutationResult(ConversationSnapshot Conversation, ConversationActionCapabilities Capabilities);

/// <summary>Resolved direct-chat target without forcing callers to scan characters themselves.</summary>
public sealed record DirectConversationTarget(Guid CharacterId, Guid ChatId, string CharacterName, string ChatName);
