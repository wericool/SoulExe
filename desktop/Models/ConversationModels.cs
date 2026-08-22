using System;
using System.Collections.Generic;
using System.Linq;

namespace SoulTextWpf.Models;

/// <summary>
/// A read model shared by every dialogue-like entity. It intentionally does not replace
/// SoulChat or SoulScene yet: adapters expose the common shape while existing JSON stays intact.
/// </summary>
public sealed class ConversationSnapshot
{
    public Guid Id { get; init; }
    public ConversationKind Kind { get; init; }
    public ConversationSource Source { get; init; }
    public string Name { get; init; } = "";
    public bool IsPinned { get; init; }
    public bool IsArchived { get; init; }
    public string SummaryText { get; init; } = "";
    public int LastSummarizedSequence { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<ConversationParticipant> Participants { get; init; } = [];
    public IReadOnlyList<ConversationMessageSnapshot> Messages { get; init; } = [];
    public ConversationContextSnapshot Context { get; init; } = new();
    public ConversationTurnState? TurnState { get; init; }

    public ConversationParticipant? FindParticipant(Guid? participantId) =>
        participantId is null ? null : Participants.FirstOrDefault(participant => participant.Id == participantId.Value);
}

public enum ConversationKind { Direct, Scene }
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
    public Guid Id { get; init; }
    public int SequenceNumber { get; init; }
    public ConversationMessageKind Kind { get; init; }
    public Guid? AuthorParticipantId { get; init; }
    public string AuthorName { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? EditedAt { get; init; }
    public IReadOnlyList<ConversationMessageVariantSnapshot> Variants { get; init; } = [];
    public IReadOnlyList<ConversationAttachmentSnapshot> Attachments { get; init; } = [];
}

public sealed record ConversationMessageVariantSnapshot(Guid Id, string Label, string Content, DateTimeOffset CreatedAt);
public sealed record ConversationAttachmentSnapshot(Guid Id, string MediaType, string LocalPath, string OriginalName, DateTimeOffset CreatedAt);

/// <summary>Shared facts available to a context builder; mode-specific policy stays separate.</summary>
public sealed class ConversationContextSnapshot
{
    public string InitialUserProfile { get; init; } = "";
    public string InitialRelationshipContext { get; init; } = "";
    public string Scenario { get; init; } = "";
    public string Location { get; init; } = "";
    public string TimeContext { get; init; } = "";
    public string Mood { get; init; } = "";
    public string Goal { get; init; } = "";
    public string RelationshipContext { get; init; } = "";
    public SoulMemoryBundle? Memory { get; init; }
    public IReadOnlyDictionary<Guid, string> StateValues { get; init; } = new Dictionary<Guid, string>();
}

/// <summary>Present only for modes that have a generated next turn, currently scenes.</summary>
public sealed record ConversationTurnState(
    string Status,
    string Mode,
    Guid? NextParticipantId,
    DateTimeOffset? NextTurnAt,
    int DelaySeconds,
    bool EnforceContract,
    bool AdvanceAndAvoidRepetition);
