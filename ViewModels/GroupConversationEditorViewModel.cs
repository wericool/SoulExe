using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Flat editable presentation over a canonical group conversation.</summary>
public sealed class GroupConversationEditorViewModel
{
    public GroupConversationEditorViewModel(ConversationSnapshot conversation)
    {
        Conversation = conversation;
    }

    public ConversationSnapshot Conversation { get; }
    public Guid Id => Conversation.Id;
    public string Name { get => Conversation.Name; set => Conversation.Name = value; }
    public string Scenario { get => Conversation.Context.Scenario; set => Conversation.Context.Scenario = value; }
    public string Location { get => Conversation.Context.Location; set => Conversation.Context.Location = value; }
    public string TimeContext { get => Conversation.Context.TimeContext; set => Conversation.Context.TimeContext = value; }
    public string Mood { get => Conversation.Context.Mood; set => Conversation.Context.Mood = value; }
    public string Goal { get => Conversation.Context.Goal; set => Conversation.Context.Goal = value; }
    public string RelationshipContext { get => Conversation.Context.RelationshipContext; set => Conversation.Context.RelationshipContext = value; }
    public string SummaryText => Conversation.SummaryText;
    public string Status => Conversation.TurnState?.Status ?? SceneStatus.Paused;
    public string TurnMode { get => RequireTurn().Mode; set => RequireTurn().Mode = value; }
    public int DelaySeconds { get => RequireTurn().DelaySeconds; set => RequireTurn().DelaySeconds = Math.Max(0, value); }
    public bool EnforceConversationContract { get => RequireTurn().EnforceContract; set => RequireTurn().EnforceContract = value; }
    public bool AdvanceAndAvoidRepetition { get => RequireTurn().AdvanceAndAvoidRepetition; set => RequireTurn().AdvanceAndAvoidRepetition = value; }

    public Guid? NextCharacterId
    {
        get => Conversation.FindParticipant(Conversation.TurnState?.NextParticipantId)?.CharacterId;
        set
        {
            var participant = Conversation.Participants.FirstOrDefault(item =>
                item.Kind == ConversationParticipantKind.Character && item.CharacterId == value);
            RequireTurn().NextParticipantId = participant?.Id;
        }
    }

    public IReadOnlyList<Guid> CharacterIds => Conversation.Participants
        .Where(item => item.Kind == ConversationParticipantKind.Character && item.CharacterId is not null)
        .OrderBy(item => item.SortOrder)
        .Select(item => item.CharacterId!.Value)
        .ToList();

    private ConversationTurnState RequireTurn() => Conversation.TurnState
        ?? throw new InvalidOperationException("У группового разговора отсутствует состояние хода.");
}
