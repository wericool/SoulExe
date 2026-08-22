namespace SoulTextWpf.Models;

/// <summary>Identifies an adapted conversation without introducing a persisted Conversations collection.</summary>
public sealed record ConversationAddress(Guid Id, ConversationKind Kind);

/// <summary>Explicit, transport-neutral actions a client may present for a conversation.</summary>
public sealed record ConversationActionCapabilities(
    bool CanAppendUserMessage,
    bool CanAddDirectorEvent,
    bool CanStart,
    bool CanPause,
    bool CanFinish,
    bool CanChooseNextParticipant,
    bool CanGenerateNextTurn);

public static class ConversationCapabilityPolicy
{
    public static ConversationActionCapabilities For(ConversationKind kind) => kind switch
    {
        ConversationKind.Direct => new(true, false, false, false, false, false, true),
        ConversationKind.Scene => new(false, true, true, true, true, true, true),
        _ => new(false, false, false, false, false, false, false)
    };
}

public enum ConversationSceneStatusAction { Start, Pause, Finish }

public sealed record ConversationMutationResult(ConversationSnapshot Conversation, ConversationActionCapabilities Capabilities);
