using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>
/// Read-only visual projection of a <see cref="ConversationSnapshot"/>. It is intentionally
/// separate from the legacy editing and streaming view-models while the data migration is still
/// guarded by compatibility checks.
/// </summary>
public sealed class ConversationThreadPresentationViewModel
{
    public ConversationThreadPresentationViewModel(ConversationSnapshot snapshot, IEnumerable<SoulCharacter> characters)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var characterById = (characters ?? []).ToDictionary(character => character.Id);
        Id = snapshot.Id;
        Title = snapshot.Name;
        KindLabel = snapshot.Mode == ConversationMode.Group ? "Групповой разговор" : "Личный разговор";
        IsScene = snapshot.Mode == ConversationMode.Group;

        var participants = snapshot.Participants.ToDictionary(participant => participant.Id);
        var primaryCharacterId = snapshot.Participants
            .Where(participant => participant.Kind == ConversationParticipantKind.Character)
            .OrderBy(participant => participant.SortOrder)
            .Select(participant => participant.Id)
            .FirstOrDefault();
        var previousDate = DateOnly.MinValue;
        foreach (var message in snapshot.Messages.OrderBy(message => message.SequenceNumber))
        {
            participants.TryGetValue(message.AuthorParticipantId ?? Guid.Empty, out var participant);
            var date = DateOnly.FromDateTime(message.CreatedAt.LocalDateTime.Date);
            Messages.Add(new ConversationThreadMessagePresentationViewModel(
                message,
                participant,
                date != previousDate,
                FormatDateSeparator(date),
                IsScene ? message.AuthorParticipantId == primaryCharacterId : participant?.Kind == ConversationParticipantKind.User,
                participant?.CharacterId is { } characterId && characterById.TryGetValue(characterId, out var character) ? character.AvatarPath : ""));
            previousDate = date;
        }
    }

    public Guid Id { get; }
    public string Title { get; }
    public string KindLabel { get; }
    public bool IsScene { get; }
    public ObservableCollection<ConversationThreadMessagePresentationViewModel> Messages { get; } = [];

    private static string FormatDateSeparator(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date == today) return "Сегодня";
        if (date == today.AddDays(-1)) return "Вчера";
        return date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    }
}

public sealed class ConversationThreadMessagePresentationViewModel
{
    public ConversationThreadMessagePresentationViewModel(
        ConversationMessageSnapshot message,
        ConversationParticipant? participant,
        bool showDateSeparator,
        string dateSeparatorLabel,
        bool isOutgoing,
        string avatarPath)
    {
        Id = message.Id;
        Content = message.Content;
        AuthorName = string.IsNullOrWhiteSpace(message.AuthorName) ? participant?.DisplayName ?? "Система" : message.AuthorName;
        AuthorInitials = Initials(AuthorName);
        AvatarPath = avatarPath;
        Time = message.CreatedAt.LocalDateTime.ToString("HH:mm");
        ShowDateSeparator = showDateSeparator;
        DateSeparatorLabel = dateSeparatorLabel;
        IsOutgoing = isOutgoing;
        IsDirector = message.Kind == ConversationMessageKind.DirectorEvent;
        IsSystem = message.Kind == ConversationMessageKind.SystemEvent;
    }

    public Guid Id { get; }
    public string Content { get; }
    public string AuthorName { get; }
    public string AuthorInitials { get; }
    public string AvatarPath { get; }
    public string Time { get; }
    public bool ShowDateSeparator { get; }
    public string DateSeparatorLabel { get; }
    public bool IsOutgoing { get; }
    public bool IsDirector { get; }
    public bool IsSystem { get; }

    private static string Initials(string value) => InitialsHelper.FromName(value);
}
