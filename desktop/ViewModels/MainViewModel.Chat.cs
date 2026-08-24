using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private void RefreshChatListItem(Guid characterId, Guid chatId)
    {
        foreach (var item in ChatListItems.Where(item => item.CharacterId == characterId && item.ChatId == chatId)) item.Refresh();
        if (SelectedCharacter?.Id == characterId && SelectedPersonalConversation?.Id == chatId) RaiseChatPresentationProperties();
    }
    private void AdoptPersonalConversation(ConversationSnapshot conversation)
    {
        SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
        _conversationSnapshots = _conversationSnapshots.Where(value => value.Id != conversation.Id).Append(conversation).ToList();
    }
    private void RebuildConversationItems()
    {
        var selectedWasScene = SelectedConversationItem?.IsScene == true;
        var selectedId = SelectedConversationItem?.Id ?? (SelectedPersonalConversation?.Id ?? SelectedGroupConversation?.Id ?? Guid.Empty);
        var ordered = ConversationListBuilder.BuildConversationItems(Characters, _conversationSnapshots, ChatSearchQuery, ChatCharacterSortMode);
        ConversationItems.Clear();
        foreach (var item in ordered) ConversationItems.Add(item);
        _selectedConversationItem = ConversationListBuilder.RestoreSelection(
            ConversationItems, selectedId, selectedWasScene, SelectedGroupConversation?.Id, SelectedPersonalConversation?.Id);
        OnPropertyChanged(nameof(SelectedConversationItem));
        OnPropertyChanged(nameof(ConversationItems));
        OnPropertyChanged(nameof(IsSceneChatActive));
    }
    private async Task OpenConversationItemAsync(ConversationListItemViewModel item)
    {
        if (item.IsScene)
        {
            IsChatMessageSearchOpen = false;
            ChatMessageSearchQuery = "";
            await LoadSelectedSceneAsync(item.Id);
            CurrentPage = "Chat";
            OnPropertyChanged(nameof(IsSceneChatActive));
            return;
        }
        if (item.ChatItem is not null)
        {
            await OpenChatListItemAsync(item.ChatItem);
            OnPropertyChanged(nameof(IsSceneChatActive));
        }
    }
    private void RaiseChatPresentationProperties()
    {
        OnPropertyChanged(nameof(SelectedChatHeaderTitle));
        OnPropertyChanged(nameof(SelectedChatLastMessageLabel));
        OnPropertyChanged(nameof(SelectedCharacterPresence));
        OnPropertyChanged(nameof(SelectedCharacterCreatedLabel));
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        OnPropertyChanged(nameof(SelectedCharacterTitle));
        OnPropertyChanged(nameof(IsCharacterDescriptionExpanded));
        OnPropertyChanged(nameof(IsCharacterPersonalityExpanded));
        OnPropertyChanged(nameof(IsCharacterScenarioExpanded));
        OnPropertyChanged(nameof(SelectedCharacterDescriptionDisplay));
        OnPropertyChanged(nameof(SelectedCharacterPersonalityDisplay));
        OnPropertyChanged(nameof(SelectedCharacterScenarioDisplay));
        OnPropertyChanged(nameof(CharacterDescriptionToggleText));
        OnPropertyChanged(nameof(CharacterPersonalityToggleText));
        OnPropertyChanged(nameof(CharacterScenarioToggleText));
        OnPropertyChanged(nameof(HasSelectedCharacterDescriptionOverflow));
        OnPropertyChanged(nameof(HasSelectedCharacterPersonalityOverflow));
        OnPropertyChanged(nameof(HasSelectedCharacterScenarioOverflow));
        OnPropertyChanged(nameof(SelectedCharacterPersonalityTags));
        OnPropertyChanged(nameof(HasSelectedCharacterPersonalityTags));
    }
    private async Task LoadChatsAsync()
    {
        Messages.Clear();
        if (SelectedCharacter is null) return;
        var fresh = await _library.GetCharacterAsync(SelectedCharacter.Id);
        if (fresh is null) return;
        var selectedIndex = Characters.IndexOf(SelectedCharacter);
        if (selectedIndex >= 0) Characters[selectedIndex] = fresh;
        _selectedCharacter = fresh;
        _isCharacterDescriptionExpanded = false;
        _isCharacterPersonalityExpanded = false;
        _isCharacterScenarioExpanded = false;
        OnPropertyChanged(nameof(SelectedCharacter));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveArchitectureEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryPreset));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        OnPropertyChanged(nameof(SelectedCharacterPersonaId));
        OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
        RaiseChatPresentationProperties();
        var personalConversations = _conversationSnapshots.Where(value => value.Mode == ConversationMode.Personal && value.Participants.Any(participant => participant.CharacterId == fresh.Id) && !value.IsArchived).ToList();
        var personalSnapshot = personalConversations.FirstOrDefault(value => value.Id == fresh.CurrentChatId) ?? personalConversations.FirstOrDefault();
        SelectedPersonalConversation = personalSnapshot is null ? null : new PersonalConversationEditorViewModel(personalSnapshot);
        IsChatMessageSearchOpen = false;
        ChatMessageSearchQuery = "";
        RebuildChatCharacters();
        ToggleChatMessageSearchCommand.RaiseCanExecuteChanged();
        ContinueChatCommand.RaiseCanExecuteChanged();
        RaiseChatPresentationProperties();
        LoadMessages();
        RefreshStateVariableValues();
    }
    private void LoadMessages()
    {
        Messages.Clear();
        if (SelectedPersonalConversation is null) return;
        // При открытии длинного диалога показываем свежий фрагмент; полная история остаётся в JSON и поиске.
        foreach (var view in ChatMessageTimeline.BuildRecent(SelectedPersonalConversation.Conversation, SelectedCharacter?.AvatarPath))
            Messages.Add(view);
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        OnPropertyChanged(nameof(SelectedChatLastMessageLabel));
        RefreshChatMessageSearchResults();
    }
    private async Task OpenChatListItemAsync(ChatListItemViewModel item)
    {
        try
        {
            await _conversations.SelectPersonalAsync(item.CharacterId, item.ChatId);
            await ReloadCharactersAsync(item.CharacterId);
            CurrentPage = "Chat";
        }
        catch (Exception ex) { HandleError("Не удалось открыть выбранный чат", ex); }
    }
    private void OpenChatActionMenu(ChatListItemViewModel? item)
    {
        if (item is null) return;
        if (ChatActionMenuItem is not null && ChatActionMenuItem != item) ChatActionMenuItem.IsActionMenuOpen = false;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        ChatActionMenuItem = item;
        IsMessageActionMenuOpen = false;
        IsChatActionMenuOpen = true;
        item.IsActionMenuOpen = true;
    }
    private void CloseChatActionMenu()
    {
        if (ChatActionMenuItem is not null) ChatActionMenuItem.IsActionMenuOpen = false;
        IsChatActionMenuOpen = false;
        ChatActionMenuItem = null;
    }
    private void OpenMessageActionMenu(ChatMessageViewModel? item)
    {
        if (item is null) return;
        if (MessageActionMenuItem is not null && MessageActionMenuItem != item) MessageActionMenuItem.IsActionMenuOpen = false;
        if (ChatActionMenuItem is not null) ChatActionMenuItem.IsActionMenuOpen = false;
        MessageActionMenuItem = item;
        IsChatActionMenuOpen = false;
        IsMessageActionMenuOpen = true;
        item.IsActionMenuOpen = true;
    }
    private async Task CreateNewConversationAsync()
    {
        if (IsNewSceneType)
        {
            if (SceneCharacterA is null || SceneCharacterB is null || SceneCharacterA.Id == SceneCharacterB.Id) return;
            IsNewChatCharacterPickerOpen = false;
            await CreateSceneAsync();
            NewConversationType = "chat";
            return;
        }
        await CreateChatForNewChatCharacterAsync();
    }
    private async Task ToggleChatPinnedAsync(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        IsChatActionMenuOpen = false;
        try
        {
            IsBusy = true;
            var pinned = !item.IsPinned;
            await _conversations.SetPinnedAsync(ConversationAddress.Direct(item.ChatId), pinned);
            await ReloadCharactersAsync(item.CharacterId);
            Status = pinned
                ? $"Чат «{item.ChatName}» закреплён вверху списка."
                : $"Чат «{item.ChatName}» откреплён.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить закрепление чата", ex); }
        finally { IsBusy = false; }
    }
    private async Task DeleteChatListItemAsync(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        IsChatActionMenuOpen = false;
        try
        {
            IsBusy = true;
            await _conversations.DeleteAsync(ConversationAddress.Direct(item.ChatId));
            await ReloadCharactersAsync(item.CharacterId);
            Status = $"Удалён чат «{item.ChatName}».";
        }
        catch (Exception ex) { HandleError("Не удалось удалить чат", ex); }
        finally { IsBusy = false; }
    }
    private void BeginRenameChat(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        CloseChatActionMenu();
        RenameChatItem = item;
        RenameChatNameDraft = item.ChatName;
        IsRenameChatDialogOpen = true;
    }
    private void CloseRenameChatDialog()
    {
        IsRenameChatDialogOpen = false;
        RenameChatItem = null;
        RenameChatNameDraft = "";
    }
    private void CancelRenameChat(ChatListItemViewModel? item)
    {
        if (item is null) return;
        item.ChatNameDraft = item.ChatName;
        item.IsRenaming = false;
        SaveRenameChatCommand.RaiseCanExecuteChanged();
        CancelRenameChatCommand.RaiseCanExecuteChanged();
    }
    private async Task SaveRenameChatAsync(ChatListItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            await _conversations.RenameAsync(ConversationAddress.Direct(item.ChatId), item.ChatNameDraft);
            item.IsRenaming = false;
            SaveRenameChatCommand.RaiseCanExecuteChanged();
            CancelRenameChatCommand.RaiseCanExecuteChanged();
            await ReloadCharactersAsync(item.CharacterId);
            Status = "Название чата сохранено.";
        }
        catch (Exception ex) { HandleError("Не удалось переименовать чат", ex); }
        finally { IsBusy = false; }
    }
    private async Task SaveChatStartingContextAsync()
    {
        var character = SelectedCharacter;
        var conversation = SelectedPersonalConversation;
        if (character is null || conversation is null) return;
        try
        {
            IsBusy = true;
            await _conversations.UpdatePersonalContextAsync(conversation.Id, conversation.InitialUserProfile, conversation.InitialRelationshipContext);
            await ReloadCharactersAsync(character.Id);
            Status = "Стартовый профиль и отношения сохранены для выбранного чата.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить стартовый контекст чата", ex); }
        finally { IsBusy = false; }
    }
    private void RefreshChatMessageSearchResults() =>
        RefreshMessageSearchResults(ChatMessageSearchResults, Messages, null, ChatMessageSearchQuery, SelectedPersonalConversation?.Conversation, null);

    private void SelectChatMessageSearchResult(ChatMessageSearchResult? result) =>
        SelectSearchResult(result, Messages, null, msg => ((ChatMessageViewModel)msg).MessageId == result!.MessageId);
    private async Task DeleteChatAsync()
    {
        if (SelectedCharacter is null || SelectedPersonalConversation is null) return;
        try
        {
            IsBusy = true;
            await _conversations.DeleteAsync(ConversationAddress.Direct(SelectedPersonalConversation.Id));
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = "Чат удалён.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить чат", ex); }
        finally { IsBusy = false; }
    }
}
