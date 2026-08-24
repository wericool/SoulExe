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
    private async Task ReloadCharactersAsync(Guid? selectId = null)
    {
        var characters = await _library.GetCharactersAsync();
        _conversationSnapshots = await _conversations.GetAllAsync();
        Characters.Clear();
        foreach (var character in characters) Characters.Add(character);
        RebuildHomeCards();
        var target = selectId is not null ? Characters.FirstOrDefault(x => x.Id == selectId) : Characters.FirstOrDefault();
        _selectedCharacter = target;
        RebuildChatCharacters();
        foreach (var name in CharacterSelectionNotifications.AfterReload) OnPropertyChanged(name);
        await LoadChatsAsync();
        EnsureSceneDraftParticipants();
    }
    private void RebuildHomeCards()
    {
        HomeCards.Clear();
        foreach (var card in HomeCharacterCards.Build(Characters, _conversationSnapshots, HomeCharacterSortMode))
            HomeCards.Add(card);
    }
    private void RebuildChatCharacters()
    {
        var selectedCharacterId = SelectedCharacter?.Id;
        var selectedChatId = SelectedPersonalConversation?.Id;
        var ordered = ConversationListBuilder.BuildChatListItems(Characters, _conversationSnapshots, ChatSearchQuery, ChatCharacterSortMode);

        ChatListItems.Clear();
        foreach (var item in ordered) ChatListItems.Add(item);

        var restored = selectedChatId is null ? null : ChatListItems.FirstOrDefault(item => item.ChatId == selectedChatId && item.CharacterId == selectedCharacterId);
        _selectedChatListItem = restored;
        OnPropertyChanged(nameof(SelectedChatListItem));
        OnPropertyChanged(nameof(ChatListItems));
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        RebuildConversationItems();
    }
    private async Task SelectCharacterAsync(SoulCharacter? character)
    {
        if (character is null) return;
        await LoadChatsAsync();
        RefreshLorebookBindingFlag();
        RaiseAllCommands();
    }
    private Task AddCharacterAsync()
    {
        OpenCharacterCreationDialog();
        return Task.CompletedTask;
    }
    private void OpenCharacterCreationDialog()
    {
        CharacterCreationMode = string.Empty;
        CharacterNameDraft = string.Empty;
        CharacterGenerationIdea = string.Empty;
        IsCharacterCreationDialogOpen = true;
    }
    private void SelectCharacterCreationMode(string? mode)
    {
        CharacterCreationMode = mode is "manual" or "generate" ? mode : string.Empty;
    }
    private void CloseCharacterCreationDialog()
    {
        IsCharacterCreationDialogOpen = false;
        CharacterCreationMode = string.Empty;
        CharacterNameDraft = string.Empty;
        CharacterGenerationIdea = string.Empty;
    }
    private async Task CreateCharacterWithNameAsync()
    {
        var name = CharacterNameDraft.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            IsBusy = true;
            var character = await _library.CreateCharacterAsync(name);
            await ReloadCharactersAsync(character.Id);
            CharacterEditorTab = "info";
            CloseCharacterCreationDialog();
            CurrentPage = "Characters";
            Status = $"Создан персонаж «{character.Name}». Заполните карточку и сохраните изменения.";
        }
        catch (Exception ex) { HandleError("Не удалось создать персонажа", ex); }
        finally { IsBusy = false; }
    }
    private async Task ExportCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        var dialog = new SaveFileDialog { Filter = "Character Card JSON|*.json|Character Card PNG (нужен PNG-аватар)|*.png", FileName = SafeFileNames.ForExport(SelectedCharacter.Name), AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _characterCardExporter.ExportAsync(SelectedCharacter, dialog.FileName);
            Status = $"Карточка экспортирована: {dialog.FileName}";
        }
        catch (Exception ex) { HandleError("Не удалось экспортировать карточку", ex); }
        finally { IsBusy = false; }
    }
    private async Task ImportSoulOfWaifuAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку старой установки Soul-of-Waifu" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var report = await _soulOfWaifuImporter.ImportAsync(dialog.FolderName);
            await ReloadCharactersAsync();
            Status = report.ToDisplayText();
        }
        catch (Exception ex) { HandleError("Не удалось перенести данные Soul-of-Waifu", ex); }
        finally { IsBusy = false; }
    }
    private async Task ImportCharacterAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Character Card V2|*.png;*.json|PNG image|*.png|JSON card|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var character = await _characterCards.ImportAsync(dialog.FileName);
            await ReloadCharactersAsync(character.Id);
            Status = $"Импортирован персонаж «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось импортировать карточку", ex); }
        finally { IsBusy = false; }
    }
    private async Task OpenCharacterChatAsync(SoulCharacter? character)
    {
        if (character is null) return;
        try
        {
            IsBusy = true;
            var created = await _conversations.CreateAsync([character.Id], "Новый разговор");
            var conversation = created.Conversation;
            // Сбрасываем старый элемент общего списка: иначе он имеет приоритет и визуально остаётся открытым.
            _selectedConversationItem = null;
            OnPropertyChanged(nameof(SelectedConversationItem));
            OnPropertyChanged(nameof(IsSceneChatActive));
            await ReloadCharactersAsync(character.Id);
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
            LoadMessages();
            RebuildConversationItems();
            CurrentPage = "Chat";
            Status = $"Создан и открыт чат «{conversation.Name}» с персонажем «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать чат для персонажа", ex); }
        finally { IsBusy = false; }
    }
    private async Task OpenCharacterEditorAsync(SoulCharacter? character)
    {
        if (character is null) return;
        try
        {
            IsBusy = true;
            await ReloadCharactersAsync(character.Id);
            CharacterEditorTab = "info";
            CurrentPage = "Characters";
            Status = $"Открыта карточка персонажа «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось открыть редактор персонажа", ex); }
        finally { IsBusy = false; }
    }
    private Task ConfirmDeleteCharacterAsync(SoulCharacter? character)
    {
        if (character is null || Characters.Count <= 1) return Task.CompletedTask;
        CharacterPendingDeletion = character;
        return Task.CompletedTask;
    }
    private async Task ConfirmCharacterDeleteAsync()
    {
        var character = CharacterPendingDeletion;
        if (character is null) return;
        CharacterPendingDeletion = null;
        SelectedCharacter = character;
        await DeleteCharacterAsync();
    }
    private async Task DeleteCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        try
        {
            IsBusy = true;
            await _library.DeleteCharacterAsync(SelectedCharacter.Id);
            await ReloadCharactersAsync();
            Status = "Персонаж удалён.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить персонажа", ex); }
        finally { IsBusy = false; }
    }
    private Task OpenNewChatCharacterPickerAsync()
    {
        NewConversationType = "chat";
        NewChatCharacter = SelectedCharacter ?? Characters.FirstOrDefault();
        EnsureSceneDraftParticipants();
        NewChatNameDraft = "Новый чат";
        IsNewChatCharacterPickerOpen = true;
        return Task.CompletedTask;
    }
    private async Task CreateChatForNewChatCharacterAsync()
    {
        var character = NewChatCharacter;
        if (character is null) return;
        var name = string.IsNullOrWhiteSpace(NewChatNameDraft) ? "Новый чат" : NewChatNameDraft.Trim();
        IsNewChatCharacterPickerOpen = false;
        await CreateChatForCharacterIdAsync(character.Id, character.Name, name);
        NewChatNameDraft = "Новый чат";
    }
    private Task CreateChatForCharacterAsync(ChatListItemViewModel? item) =>
        item is null ? Task.CompletedTask : CreateChatForCharacterIdAsync(item.CharacterId, item.CharacterName, "Новый чат");
    private async Task SaveCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        try
        {
            var characterId = SelectedCharacter.Id;
            await _library.UpdateCharacterAsync(SelectedCharacter);
            await ReloadCharactersAsync(characterId);
            Status = "Карточка персонажа сохранена.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить карточку", ex); }
    }
    private void ChooseAvatar()
    {
        if (SelectedCharacter is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SelectedCharacter.AvatarPath = LocalMediaStore.CopyAvatar(dialog.FileName, SelectedCharacter.Id, AppServices.Paths.AvatarDirectory);
            _ = SaveCharacterAsync();
            OnPropertyChanged(nameof(SelectedCharacter));
        }
        catch (Exception ex) { HandleError("Не удалось сохранить фото-аватар", ex); }
    }
}
