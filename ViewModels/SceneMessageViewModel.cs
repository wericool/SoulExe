using System.ComponentModel;
using SoulExe.Models;

namespace SoulExe.ViewModels;

public sealed class SceneMessageViewModel : INotifyPropertyChanged
{
    private string _content;
    private readonly bool _isLive;
    private readonly string _avatarPath;
    private bool _isSearchHighlighted;

    private SceneMessageViewModel(Guid id, string speakerName, string content, string time, bool isDirector, bool isFirstCharacter, bool isUserParticipant, bool isLive, string? avatarPath)
    {
        Id = id;
        SpeakerName = speakerName;
        _content = content;
        Time = time;
        IsDirector = isDirector;
        IsFirstCharacter = isFirstCharacter;
        IsUserParticipant = isUserParticipant;
        _isLive = isLive;
        _avatarPath = avatarPath ?? "";
    }

    public SceneMessageViewModel(ConversationSnapshot conversation, ConversationMessageSnapshot message, Guid firstCharacterId, string? avatarPath = null)
        : this(
            message.Id,
            message.AuthorName,
            CurrentContent(message),
            message.CreatedAt.ToString("HH:mm"),
            message.Kind == ConversationMessageKind.DirectorEvent,
            conversation.FindParticipant(message.AuthorParticipantId)?.CharacterId == firstCharacterId,
            conversation.FindParticipant(message.AuthorParticipantId)?.Kind == ConversationParticipantKind.User,
            false,
            avatarPath)
    {
    }

    private static string CurrentContent(ConversationMessageSnapshot message) =>
        (message.Variants.FirstOrDefault(value => value.Id == message.SelectedVariantId) ?? message.Variants.FirstOrDefault())?.Content
        ?? message.Content;

    public static SceneMessageViewModel Live(string speakerName, bool isFirstCharacter, string? avatarPath = null) =>
        new(Guid.NewGuid(), speakerName, "", DateTime.Now.ToString("HH:mm"), false, isFirstCharacter, false, true, avatarPath);

    public Guid Id { get; }
    public string SpeakerName { get; }
    public string SpeakerInitials => InitialsHelper.FromName(SpeakerName);
    public string AvatarPath => _avatarPath;
    public string Content
    {
        get => _content;
        private set
        {
            if (_content == value) return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }
    }
    public string Time { get; }
    public bool IsDirector { get; }
    public bool IsFirstCharacter { get; }
    public bool IsSecondCharacter => !IsDirector && !IsUserParticipant && !IsFirstCharacter;
    public bool IsUserParticipant { get; }
    public bool IsLive => _isLive;
    public bool IsSearchHighlighted => _isSearchHighlighted;

    public void SetSearchHighlighted(bool value)
    {
        if (_isSearchHighlighted == value) return;
        _isSearchHighlighted = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchHighlighted)));
    }

    public void Append(string chunk) => Content += chunk;
    public void SetContent(string value) => Content = value ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
}
