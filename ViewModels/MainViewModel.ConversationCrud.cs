using System.Collections.ObjectModel;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private ConversationAddress? AddressForItem(ConversationListItemViewModel item) =>
        item.IsScene ? ConversationAddress.Scene(item.Id) : item.ChatItem is not null ? ConversationAddress.Direct(item.ChatItem.ChatId) : null;

    private string DisplayNameForItem(ConversationListItemViewModel item) =>
        item.IsScene ? item.Conversation.Name : item.ChatItem?.ChatName ?? item.Title;

    private async Task ReloadAfterMutationAsync(ConversationListItemViewModel item)
    {
        if (item.IsScene) await ReloadScenesAsync(item.Id);
        else if (item.ChatItem is not null) await ReloadCharactersAsync(item.ChatItem.CharacterId);
    }

    private async Task PerformConversationActionAsync(ConversationListItemViewModel? item, Func<ConversationAddress, Task> action, string actionLabel, string? errorLabel = null)
    {
        if (item is null) return;
        var address = AddressForItem(item);
        if (address is null) return;
        var displayName = DisplayNameForItem(item);
        try
        {
            IsBusy = true;
            await action(address);
            await ReloadAfterMutationAsync(item);
            Status = $"{actionLabel} «{displayName}».";
        }
        catch (Exception ex) { HandleError(errorLabel ?? $"Не удалось выполнить действие для «{displayName}»", ex); }
        finally { IsBusy = false; }
    }

    private async Task ToggleConversationPinnedAsync(ConversationListItemViewModel? item)
    {
        if (item is null) return;
        var address = AddressForItem(item);
        if (address is null) return;
        await PerformConversationActionAsync(item,
            async addr =>
            {
                var pinned = !item.IsPinned;
                await _conversations.SetPinnedAsync(addr, pinned);
                Status = pinned
                    ? $"{(item.IsScene ? "Групповой разговор" : "Личный разговор")} «{DisplayNameForItem(item)}» закреплён вверху списка."
                    : $"{(item.IsScene ? "Групповой разговор" : "Личный разговор")} «{DisplayNameForItem(item)}» откреплён.";
            },
            item.IsPinned ? "Открепление" : "Закрепление",
            "Не удалось изменить закрепление");
    }

    private Task RequestConversationDeletionAsync(ConversationListItemViewModel? item)
    {
        if (item is null) return Task.CompletedTask;
        var type = item.IsScene ? "групповой разговор" : "личный разговор";
        var address = AddressForItem(item);
        if (address is null) return Task.CompletedTask;
        var conversationId = item.Id;
        var characterId = item.ChatItem?.CharacterId;
        var displayName = DisplayNameForItem(item);
        PendingDeletion = new PendingDeletionRequest(
            $"Удалить {type}?",
            $"{type[..1].ToUpperInvariant()}{type[1..]} «{displayName}» будет удалён.",
            "Это действие нельзя отменить: история и связанные локальные данные разговора будут удалены.",
            () => DeleteConversationAsync(address, conversationId, characterId, item.IsScene, displayName));
        return Task.CompletedTask;
    }

    private async Task DeleteConversationAsync(ConversationAddress address, Guid conversationId, Guid? characterId, bool isScene, string displayName)
    {
        if (isScene)
        {
            try
            {
                IsBusy = true;
                if (SelectedGroupConversation?.Id == conversationId) CancelSceneTimer();
                _sceneTurnScheduler.Cancel(conversationId);
                await _conversations.DeleteAsync(address);
                await ReloadScenesAsync();
                Status = $"Групповой разговор «{displayName}» удалён.";
            }
            catch (Exception ex) { HandleError("Не удалось удалить групповой разговор", ex); }
            finally { IsBusy = false; }
            return;
        }
        try
        {
            IsBusy = true;
            await _conversations.DeleteAsync(address);
            if (characterId is not null) await ReloadCharactersAsync(characterId.Value);
            Status = $"Удаление «{displayName}».";
        }
        catch (Exception ex) { HandleError("Не удалось удалить чат", ex); }
        finally { IsBusy = false; }
    }

    private async Task ConfirmPendingDeletionAsync()
    {
        var pending = PendingDeletion;
        if (pending is null) return;
        PendingDeletion = null;
        try { await pending.Execute(); }
        catch (Exception ex) { HandleError("Не удалось выполнить удаление", ex); }
    }

    private void BeginRenameConversation(ConversationListItemViewModel? item)
    {
        if (item is null) return;
        if (item.IsScene)
        {
            RenameScene = item;
            RenameSceneNameDraft = item.Conversation.Name;
            IsRenameSceneDialogOpen = true;
            return;
        }
        BeginRenameChat(item.ChatItem);
    }

    private async Task ConfirmRenameConversationAsync()
    {
        if (RenameScene is not null && IsRenameSceneDialogOpen)
        {
            var item = RenameScene;
            var name = RenameSceneNameDraft.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                IsBusy = true;
                await _conversations.RenameAsync(ConversationAddress.Scene(item.Id), name);
                CloseRenameSceneDialog();
                await ReloadScenesAsync(item.Id);
                Status = "Название группового разговора сохранено.";
            }
            catch (Exception ex) { HandleError("Не удалось переименовать групповой разговор", ex); }
            finally { IsBusy = false; }
            return;
        }

        if (RenameChatItem is not null && IsRenameChatDialogOpen)
        {
            var item = RenameChatItem;
            var name = RenameChatNameDraft.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            try
            {
                IsBusy = true;
                await _conversations.RenameAsync(ConversationAddress.Direct(item.ChatId), name);
                CloseRenameChatDialog();
                await ReloadCharactersAsync(item.CharacterId);
                Status = "Название чата сохранено.";
            }
            catch (Exception ex) { HandleError("Не удалось переименовать чат", ex); }
            finally { IsBusy = false; }
        }
    }

    private void RefreshMessageSearchResults(
        ObservableCollection<ChatMessageSearchResult> results,
        IEnumerable<ChatMessageViewModel>? chatMessages,
        IEnumerable<SceneMessageViewModel>? sceneMessages,
        string query,
        ConversationSnapshot? selectedPersonal,
        ConversationSnapshot? selectedGroup)
    {
        results.Clear();
        if (chatMessages is not null)
        {
            foreach (var message in chatMessages) message.SetSearchHighlighted(false);
            foreach (var hit in ConversationMessageSearch.SearchPersonal(selectedPersonal, query))
                results.Add(hit);
        }
        else if (sceneMessages is not null)
        {
            foreach (var message in sceneMessages) message.SetSearchHighlighted(false);
            foreach (var hit in ConversationMessageSearch.SearchGroup(selectedGroup, query))
                results.Add(hit);
        }
    }

    private void SelectSearchResult(
        ChatMessageSearchResult? result,
        IEnumerable<ChatMessageViewModel>? chatMessages,
        IEnumerable<SceneMessageViewModel>? sceneMessages,
        Func<object, bool> matchPredicate)
    {
        if (result is null) return;
        EnsurePersonalMessageVisible(result.MessageId);
        SelectedChatMessageSearchResult = result;
        if (chatMessages is not null)
            foreach (var message in Messages) message.SetSearchHighlighted(matchPredicate(message));
        else if (sceneMessages is not null)
            foreach (var message in sceneMessages) message.SetSearchHighlighted(matchPredicate(message));
        Status = $"Найдена реплика от {result.AuthorName} · {result.CreatedAt.LocalDateTime:dd.MM.yyyy HH:mm}.";
    }
}
