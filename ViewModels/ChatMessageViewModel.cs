using System.ComponentModel;
using System.Windows.Input;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private readonly SoulMessage _record;
    private readonly string _avatarPath;
    private bool _isThoughtExpanded;
    private bool _isEditing;
    private bool _isSearchHighlighted;
    private bool _isActionMenuOpen;
    private bool _showDateSeparator;
    private string _dateSeparatorLabel = "";
    private string _editingContent = "";

    public ChatMessageViewModel(SoulMessage record, string? avatarPath = null)
    {
        _record = record;
        _avatarPath = avatarPath ?? "";
        ToggleThoughtCommand = new RelayCommand(_ => ToggleThought(), _ => HasThoughtContent);
    }

    public Guid MessageId => _record.Id;
    public string AuthorName => string.IsNullOrWhiteSpace(_record.AuthorName) ? (IsUser ? "Вы" : "Персонаж") : _record.AuthorName;
    public string AuthorInitials => InitialsHelper.FromName(AuthorName);
    public string AvatarPath => IsUser ? "" : _avatarPath;
    public string Content => CurrentVariant?.Content ?? "";
    public string VisibleContent => SplitThought(Content).Visible;
    public string ThoughtContent => SplitThought(Content).Thought;
    public bool HasThoughtContent => !IsUser && !string.IsNullOrWhiteSpace(ThoughtContent);
    public bool IsThoughtExpanded => _isThoughtExpanded;
    public bool IsEditing => _isEditing;
    public bool IsSearchHighlighted => _isSearchHighlighted;
    public bool IsActionMenuOpen
    {
        get => _isActionMenuOpen;
        set
        {
            if (_isActionMenuOpen == value) return;
            _isActionMenuOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActionMenuOpen)));
        }
    }
    public string EditingContent
    {
        get => _editingContent;
        set
        {
            if (_editingContent == value) return;
            _editingContent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingContent)));
        }
    }
    public bool CanContinueFromHere => IsUser;
    public bool HasResponseVariants => !IsUser && VariantCount > 1;
    public string ThoughtToggleText => IsThoughtExpanded ? "▾  Скрыть мысли" : "▸  Показать мысли";
    public RelayCommand ToggleThoughtCommand { get; }
    public bool ShowDateSeparator { get => _showDateSeparator; set { if (_showDateSeparator == value) return; _showDateSeparator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDateSeparator))); } }
    public string DateSeparatorLabel { get => _dateSeparatorLabel; set { if (_dateSeparatorLabel == value) return; _dateSeparatorLabel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateSeparatorLabel))); } }
    public string Time => _record.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsUser => _record.Role == SoulMessageRole.User;
    public bool IsDirector => _record.AuthorKind == SoulMessageAuthorKind.Director;
    public int VariantCount => _record.Variants.Count;
    public int CurrentVariantNumber => Math.Max(1, _record.Variants.FindIndex(x => x.Id == _record.CurrentVariantId) + 1);
    public bool CanMovePrevious => !IsUser && CurrentVariantNumber > 1;
    public bool CanMoveNext => !IsUser && CurrentVariantNumber < VariantCount;
    private SoulMessageVariant? CurrentVariant => _record.Variants.FirstOrDefault(x => x.Id == _record.CurrentVariantId) ?? _record.Variants.FirstOrDefault();
    public event PropertyChangedEventHandler? PropertyChanged;

    public SoulMessageVariant? GetAdjacentVariant(int direction)
    {
        var index = _record.Variants.FindIndex(x => x.Id == _record.CurrentVariantId);
        if (index < 0) index = 0;
        var target = index + direction;
        return target >= 0 && target < _record.Variants.Count ? _record.Variants[target] : null;
    }

    public void SelectVariant(Guid id)
    {
        _record.CurrentVariantId = id;
        _isThoughtExpanded = false;
        Refresh();
    }

    public void AdoptPersistedMessage(SoulMessage saved)
    {
        _record.Id = saved.Id;
        _record.SequenceNumber = saved.SequenceNumber;
        _record.Role = saved.Role;
        _record.AuthorName = saved.AuthorName;
        _record.CurrentVariantId = saved.CurrentVariantId;
        _record.Variants = saved.Variants;
        _record.Attachments = saved.Attachments;
        _record.CreatedAt = saved.CreatedAt;
        _record.EditedAt = saved.EditedAt;
        Refresh();
    }

    public void SetSearchHighlighted(bool value)
    {
        if (_isSearchHighlighted == value) return;
        _isSearchHighlighted = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchHighlighted)));
    }

    public void BeginEditing()
    {
        EditingContent = Content;
        _isEditing = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
    }

    public void CancelEditing()
    {
        _isEditing = false;
        EditingContent = "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
    }

    public void Refresh()
    {
        foreach (var name in new[] { nameof(MessageId), nameof(AuthorName), nameof(AuthorInitials), nameof(AvatarPath), nameof(Content), nameof(VisibleContent), nameof(ThoughtContent), nameof(HasThoughtContent), nameof(IsThoughtExpanded), nameof(IsEditing), nameof(IsSearchHighlighted), nameof(IsActionMenuOpen), nameof(EditingContent), nameof(Time), nameof(IsDirector), nameof(CanContinueFromHere), nameof(HasResponseVariants), nameof(ThoughtToggleText), nameof(VariantCount), nameof(CurrentVariantNumber), nameof(CanMovePrevious), nameof(CanMoveNext) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ToggleThoughtCommand.RaiseCanExecuteChanged();
    }

    public void RefreshStreamingPreview()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThoughtContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtToggleText)));
        ToggleThoughtCommand.RaiseCanExecuteChanged();
    }

    private void ToggleThought()
    {
        _isThoughtExpanded = !_isThoughtExpanded;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsThoughtExpanded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtToggleText)));
    }

    private static (string Visible, string Thought) SplitThought(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", "");
        var remaining = text;
        var thoughtParts = new List<string>();
        foreach (var tag in new[] { "think", "thinking", "thought", "reasoning" })
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            while (true)
            {
                var start = remaining.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                var end = remaining.IndexOf(close, start + open.Length, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                thoughtParts.Add(remaining[(start + open.Length)..end].Trim());
                remaining = remaining.Remove(start, end + close.Length - start);
            }
        }
        return (remaining.Trim(), string.Join("\n\n", thoughtParts.Where(x => !string.IsNullOrWhiteSpace(x))));
    }
}
