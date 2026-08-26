using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Builds in-conversation search hits for direct chats and scenes.</summary>
public static class ConversationMessageSearch
{
    public static IReadOnlyList<ChatMessageSearchResult> SearchPersonal(ConversationSnapshot? conversation, string? query)
    {
        var q = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q) || conversation?.Messages is null) return [];

        var results = new List<ChatMessageSearchResult>();
        foreach (var message in conversation.Messages.OrderBy(item => item.SequenceNumber))
        {
            var content = message.Variants.FirstOrDefault(item => item.Id == message.SelectedVariantId)?.Content
                ?? message.Variants.FirstOrDefault()?.Content
                ?? message.Content;
            if (!content.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            results.Add(new ChatMessageSearchResult(message.Id, message.AuthorName, content, message.CreatedAt));
        }
        return results;
    }

    public static IReadOnlyList<ChatMessageSearchResult> SearchGroup(ConversationSnapshot? conversation, string? query)
    {
        var q = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q) || conversation?.Messages is null) return [];

        var results = new List<ChatMessageSearchResult>();
        foreach (var message in conversation.Messages.OrderBy(item => item.SequenceNumber))
        {
            var content = (message.Variants.FirstOrDefault(item => item.Id == message.SelectedVariantId)
                ?? message.Variants.FirstOrDefault())?.Content ?? message.Content ?? string.Empty;
            if (!content.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            results.Add(new ChatMessageSearchResult(message.Id, message.AuthorName, content, message.CreatedAt));
        }
        return results;
    }
}
