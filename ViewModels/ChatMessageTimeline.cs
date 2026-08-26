using System.Globalization;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

/// <summary>Builds chat message view-models with localized date separators.</summary>
public static class ChatMessageTimeline
{
    private static readonly CultureInfo Russian = new("ru-RU");

    public static IEnumerable<ChatMessageViewModel> BuildRange(
        ConversationSnapshot conversation,
        string? avatarPath,
        int skip,
        int take)
    {
        DateOnly? previousDate = null;
        foreach (var canonical in conversation.Messages.OrderBy(x => x.SequenceNumber).Skip(skip).Take(take))
        {
            var message = ConversationMessageMapper.ToPersonalMessage(conversation, canonical);
            var view = new ChatMessageViewModel(message, avatarPath);
            var date = DateOnly.FromDateTime(message.CreatedAt.LocalDateTime.Date);
            view.ShowDateSeparator = previousDate != date;
            view.DateSeparatorLabel = message.CreatedAt.LocalDateTime.ToString("d MMMM yyyy", Russian);
            previousDate = date;
            yield return view;
        }
    }
}
