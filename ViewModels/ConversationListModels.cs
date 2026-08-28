using System.ComponentModel;
using System.Windows.Input;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed record ChatCharacterSortOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class HomeCharacterCardViewModel
{
    private HomeCharacterCardViewModel(SoulCharacter? character, bool isAddCharacter)
    {
        Character = character;
        IsAddCharacter = isAddCharacter;
    }

    public HomeCharacterCardViewModel(SoulCharacter character) : this(character, false) { }
    public SoulCharacter? Character { get; }
    public bool IsAddCharacter { get; }
    public static HomeCharacterCardViewModel AddCard() => new(null, true);
}

public sealed record ChatMessageSearchResult(Guid MessageId, string AuthorName, string Content, DateTimeOffset CreatedAt)
{
    public string DisplayAuthor => string.IsNullOrWhiteSpace(AuthorName) ? "Персонаж" : AuthorName;
    public string Timestamp => CreatedAt.LocalDateTime.ToString("dd.MM · HH:mm");
    public string Preview
    {
        get
        {
            var prepared = string.Join(" ", (Content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return prepared.Length <= 100 ? prepared : prepared[..99] + "…";
        }
    }
}

public sealed class ChatListItemViewModel : INotifyPropertyChanged
{
    private string _chatNameDraft;
    private bool _isRenaming;
    private bool _isActionMenuOpen;

    public ChatListItemViewModel(SoulCharacter character, ConversationSnapshot conversation)
    {
        Character = character;
        Conversation = conversation;
        _chatNameDraft = conversation.Name;
    }

    public SoulCharacter Character { get; }
    public ConversationSnapshot Conversation { get; }
    public Guid CharacterId => Character.Id;
    public Guid ChatId => Conversation.Id;
    public string CharacterName => Character.Name;
    public string AvatarPath => Character.AvatarPath;
    public string Initials => Character.Initials;
    public string ChatName => Conversation.Name;
    public bool IsPinned => Conversation.IsPinned;
    public string PinIcon => IsPinned ? "📌" : "";
    public string PinMenuText => IsPinned ? "📌  Открепить" : "📌  Закрепить";
    private ConversationMessageSnapshot? LastMessage => Conversation.Messages.OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.SequenceNumber).FirstOrDefault();
    public DateTimeOffset UpdatedAt => LastMessage?.CreatedAt ?? Conversation.CreatedAt;
    public string LastMessagePreview
    {
        get
        {
            var message = LastMessage;
            if (message is null) return "Нет сообщений";
            var content = message.Variants.FirstOrDefault(variant => variant.Id == message.SelectedVariantId)?.Content
                ?? message.Variants.FirstOrDefault()?.Content ?? message.Content;
            content = string.Join(" ", content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return content.Length <= 56 ? content : content[..55] + "…";
        }
    }
    public string LastMessageTime
    {
        get
        {
            var timestamp = LastMessage?.CreatedAt ?? UpdatedAt;
            return timestamp.LocalDateTime.Date == DateTime.Today
                ? timestamp.LocalDateTime.ToString("HH:mm")
                : timestamp.LocalDateTime.ToString("dd.MM");
        }
    }
    public bool CanDelete => true;
    public int MessageCount => Conversation.Messages.Count;
    public bool MatchesSearch(string query) => string.IsNullOrWhiteSpace(query)
        || CharacterName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || ChatName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || LastMessagePreview.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    public string ChatNameDraft { get => _chatNameDraft; set { if (_chatNameDraft == value) return; _chatNameDraft = value; OnPropertyChanged(nameof(ChatNameDraft)); } }
        public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged(nameof(IsRenaming));
        }
    }

    public bool IsActionMenuOpen
    {
        get => _isActionMenuOpen;
        set
        {
            if (_isActionMenuOpen == value) return;
            _isActionMenuOpen = value;
            OnPropertyChanged(nameof(IsActionMenuOpen));
        }
    }
    public void Refresh()
    {
        OnPropertyChanged(nameof(CharacterName));
        OnPropertyChanged(nameof(AvatarPath));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(ChatName));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinMenuText));
        OnPropertyChanged(nameof(UpdatedAt));
        OnPropertyChanged(nameof(LastMessagePreview));
        OnPropertyChanged(nameof(LastMessageTime));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(MessageCount));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


public sealed class ConversationListItemViewModel
{
    private ConversationListItemViewModel(ChatListItemViewModel chatItem, ConversationSnapshot conversation)
    {
        ChatItem = chatItem;
        Conversation = conversation;
        Id = chatItem.ChatId;
        Kind = ConversationKind.Direct;
        Title = chatItem.CharacterName;
        SecondaryTitle = chatItem.ChatName;
        Preview = NormalizePreview(chatItem.LastMessagePreview);
        UpdatedAt = chatItem.UpdatedAt;
        IsPinned = chatItem.IsPinned;
        AvatarAPath = chatItem.AvatarPath;
        AvatarAInitials = chatItem.Initials;
        AvatarBPath = "";
        AvatarBInitials = "";
    }

    private ConversationListItemViewModel(ConversationSnapshot conversation, SoulCharacter? first, SoulCharacter? second)
    {
        Conversation = conversation;
        Id = conversation.Id;
        Kind = ConversationKind.Scene;
        var firstName = first?.Name ?? "Персонаж A";
        var secondName = second?.Name ?? "Персонаж B";
        Title = $"{firstName}, {secondName}";
        SecondaryTitle = string.IsNullOrWhiteSpace(conversation.Name) ? "Групповой разговор" : conversation.Name;
        var last = conversation.Messages.OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.SequenceNumber).FirstOrDefault();
        Preview = NormalizePreview(string.IsNullOrWhiteSpace(last?.Content) ? conversation.Context.Scenario : last!.Content);
        UpdatedAt = last?.CreatedAt ?? conversation.CreatedAt;
        IsPinned = conversation.IsPinned;
        AvatarAPath = first?.AvatarPath ?? "";
        AvatarBPath = second?.AvatarPath ?? "";
        AvatarAInitials = Initials(firstName);
        AvatarBInitials = Initials(secondName);
    }

    private const int PreviewMaximumLength = 56;

    private static string NormalizePreview(string? text)
    {
        var preview = string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return preview.Length <= PreviewMaximumLength ? preview : preview[..(PreviewMaximumLength - 1)] + "…";
    }

    public Guid Id { get; }
    public ConversationKind Kind { get; }
    public bool IsScene => Kind == ConversationKind.Scene;
    public string TypeLabel => IsScene ? "Групповой разговор" : "Личный разговор";
    public bool HasSceneControls => Kind == ConversationKind.Scene;
    public bool IsPinned { get; }
    public ChatListItemViewModel? ChatItem { get; }
    public ConversationSnapshot Conversation { get; }
    public string Title { get; }
    public string SecondaryTitle { get; }
    public string Preview { get; }
    public DateTimeOffset UpdatedAt { get; }
    public string AvatarAPath { get; }
    public string AvatarBPath { get; }
    public string AvatarAInitials { get; }
    public string AvatarBInitials { get; }
    public bool HasSecondAvatar => IsScene;
    public string PinIcon => IsPinned ? "📌" : "";
    public string PinMenuText => IsPinned ? "📌  Открепить" : "📌  Закрепить";
    public bool HasTypeBadge => false;
    public string TypeBadge => "";
    public string TimeLabel => UpdatedAt == DateTimeOffset.MinValue ? "" : UpdatedAt.LocalDateTime.ToString("HH:mm");
    public static ConversationListItemViewModel FromPersonal(SoulCharacter character, ConversationSnapshot conversation) => new(new ChatListItemViewModel(character, conversation), conversation);
    public static ConversationListItemViewModel FromGroup(ConversationSnapshot conversation, SoulCharacter? first, SoulCharacter? second) => new(conversation, first, second);

    public bool MatchesSearch(string query) =>
        Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || SecondaryTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Preview.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string Initials(string value) => InitialsHelper.FromName(value);
}
