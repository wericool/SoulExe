using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Detects exact duplicate assistant replies in recent chat history.</summary>
public static class AssistantReplyCompare
{
    public static bool IsExactRecentDuplicate(ConversationSnapshot conversation, string assistantText)
    {
        var normalizedReply = AppLog.NormalizeForComparison(assistantText);
        return conversation.Messages
            .Where(message => conversation.FindParticipant(message.AuthorParticipantId)?.Kind == ConversationParticipantKind.Character)
            .Select(message => message.Variants.FirstOrDefault(variant => variant.Id == message.SelectedVariantId)?.Content
                               ?? message.Variants.FirstOrDefault()?.Content
                               ?? message.Content)
            .Any(previous => string.Equals(AppLog.NormalizeForComparison(previous), normalizedReply, StringComparison.Ordinal));
    }
}
