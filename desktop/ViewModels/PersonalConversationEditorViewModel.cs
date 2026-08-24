using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Editable presentation over a canonical personal conversation.</summary>
public sealed class PersonalConversationEditorViewModel
{
    public PersonalConversationEditorViewModel(ConversationSnapshot conversation) => Conversation = conversation;
    public ConversationSnapshot Conversation { get; }
    public Guid Id => Conversation.Id;
    public string Name { get => Conversation.Name; set => Conversation.Name = value; }
    public string InitialUserProfile { get => Conversation.Context.InitialUserProfile; set => Conversation.Context.InitialUserProfile = value; }
    public string InitialRelationshipContext { get => Conversation.Context.InitialRelationshipContext; set => Conversation.Context.InitialRelationshipContext = value; }
    public string SummaryText => Conversation.SummaryText;
    public SoulMemoryBundle Memory => Conversation.Context.Memory ??= new SoulMemoryBundle();
    public int MessageCount => Conversation.Messages.Count;
}
