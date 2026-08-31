using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulExe.Models;

/// <summary>
/// Canonical persisted model shared by personal and group conversations.
/// </summary>
public sealed class ConversationSnapshot
{
    public Guid Id { get; set; }
    public ConversationKind Kind { get; set; }
    public ConversationSource Source { get; set; }
    public string Name { get; set; } = "";
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public string SummaryText { get; set; } = "";
    public int LastSummarizedSequence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ConversationParticipant> Participants { get; set; } = [];
    public List<ConversationMessageSnapshot> Messages { get; set; } = [];
    public ConversationContextSnapshot Context { get; set; } = new();
    public ConversationTurnState? TurnState { get; set; }

    /// <summary>
    /// User-facing conversation mode is derived from its character participants.
    /// </summary>
    public ConversationMode Mode => Participants.Count(participant => participant.Kind == ConversationParticipantKind.Character) > 1
        ? ConversationMode.Group
        : ConversationMode.Personal;

    public ConversationParticipant? FindParticipant(Guid? participantId) =>
        participantId is null ? null : Participants.FirstOrDefault(participant => participant.Id == participantId.Value);
}

public enum ConversationKind { Direct, Scene }
public enum ConversationMode { Personal, Group }
public enum ConversationSource { CharacterChat, RootScene }
public enum ConversationParticipantKind { User, Character, Director, System }
public enum ConversationMessageKind { Message, DirectorEvent, SystemEvent }

public sealed record ConversationParticipant(
    Guid Id,
    ConversationParticipantKind Kind,
    string DisplayName,
    Guid? CharacterId,
    bool CanGenerate,
    int SortOrder);

public sealed class ConversationMessageSnapshot
{
    public Guid Id { get; set; }
    public int SequenceNumber { get; set; }
    public ConversationMessageKind Kind { get; set; }
    public Guid? AuthorParticipantId { get; set; }
    public SoulMessageAuthorKind AuthorKind { get; set; } = SoulMessageAuthorKind.User;
    public Guid? AuthorPersonaId { get; set; }
    public string AuthorAvatarPath { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? EditedAt { get; set; }
    public Guid? SelectedVariantId { get; set; }
    public List<ConversationMessageVariantSnapshot> Variants { get; set; } = [];
    public List<ConversationAttachmentSnapshot> Attachments { get; set; } = [];
}

public sealed record ConversationMessageVariantSnapshot(Guid Id, string Label, string Content, DateTimeOffset CreatedAt);
public sealed record ConversationAttachmentSnapshot(Guid Id, string MediaType, string LocalPath, string OriginalName, DateTimeOffset CreatedAt);

/// <summary>Shared facts available to a context builder; mode-specific policy stays separate.</summary>
public sealed class ConversationContextSnapshot
{
    public string InitialUserProfile { get; set; } = "";
    public string InitialRelationshipContext { get; set; } = "";
    public string SummaryDirectives { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string Location { get; set; } = "";
    public string TimeContext { get; set; } = "";
    public string Mood { get; set; } = "";
    public string Goal { get; set; } = "";
    public string RelationshipContext { get; set; } = "";
    public SoulMemoryBundle? Memory { get; set; }
    public Dictionary<Guid, string> StateValues { get; set; } = [];
    public ProactiveConversationState Proactive { get; set; } = new();
}

public sealed class ProactiveConversationState
{
    public Guid? ScheduledAfterMessageId { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string DailyCountDate { get; set; } = "";
    public int SentToday { get; set; }
}

/// <summary>Present only for modes that have a generated next turn, currently scenes.</summary>
public sealed class ConversationTurnState(
    string status,
    string mode,
    Guid? nextParticipantId,
    DateTimeOffset? nextTurnAt,
    int delaySeconds,
    bool enforceContract,
    bool advanceAndAvoidRepetition)
{
    public string Status { get; set; } = status;
    public string Mode { get; set; } = mode;
    public Guid? NextParticipantId { get; set; } = nextParticipantId;
    public DateTimeOffset? NextTurnAt { get; set; } = nextTurnAt;
    public int DelaySeconds { get; set; } = delaySeconds;
    public bool EnforceContract { get; set; } = enforceContract;
    public bool AdvanceAndAvoidRepetition { get; set; } = advanceAndAvoidRepetition;
}
