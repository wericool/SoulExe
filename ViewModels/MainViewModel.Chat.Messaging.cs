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
    private void BeginMessageEdit(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null) return;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        message.BeginEditing();
        SaveEditMessageCommand.RaiseCanExecuteChanged();
        CancelEditMessageCommand.RaiseCanExecuteChanged();
    }
    private void CancelMessageEdit(ChatMessageViewModel? message)
    {
        message?.CancelEditing();
        SaveEditMessageCommand.RaiseCanExecuteChanged();
        CancelEditMessageCommand.RaiseCanExecuteChanged();
    }
    private async Task SaveMessageEditAsync(ChatMessageViewModel? message)
    {
        if (message is null || SelectedPersonalConversation is null) return;
        var content = message.EditingContent.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            Status = "Сообщение не может быть пустым.";
            return;
        }
        try
        {
            IsBusy = true;
            var conversation = await _conversations.EditPersonalMessageAsync(SelectedPersonalConversation.Id, message.MessageId, content);
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
            LoadMessages();
            Status = "Сообщение изменено. Summary и Soul Memory этой ветки будут построены заново по обновлённой истории.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить сообщение", ex); }
        finally { IsBusy = false; }
    }
    private Task RequestMessageDeletionAsync(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null || SelectedPersonalConversation is null) return Task.CompletedTask;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        var selectedConversationId = SelectedPersonalConversation.Id;
        PendingDeletion = new PendingDeletionRequest("Удалить сообщение?", "Выбранное сообщение будет удалено из истории этого разговора.", "Это действие нельзя отменить. Контекст памяти ветки будет пересобран.", () => DeleteMessageAsync(message, selectedConversationId));
        return Task.CompletedTask;
    }
    private async Task DeleteMessageAsync(ChatMessageViewModel message, Guid conversationId)
    {
        if (SelectedPersonalConversation?.Id != conversationId) return;
        try
        {
            IsBusy = true;
            var conversation = await _conversations.DeletePersonalMessageAsync(conversationId, message.MessageId);
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
            LoadMessages();
            Status = "Сообщение удалено. Контекст памяти этой ветки будет пересобран по оставшейся истории.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить сообщение", ex); }
        finally { IsBusy = false; }
    }
    private async Task ContinueFromMessageAsync(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null || !message.CanContinueFromHere || SelectedPersonalConversation is null) return;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        try
        {
            IsBusy = true;
            var result = await _conversations.TruncatePersonalAfterAsync(SelectedPersonalConversation.Id, message.MessageId);
            var removed = result.Removed;
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(result.Conversation);
            LoadMessages();
            Status = removed == 0
                ? "Это уже последнее сообщение чата. Можно продолжать историю."
                : $"Создана новая ветка: удалено последующих сообщений: {removed}. Введите новое сообщение для продолжения.";
        }
        catch (Exception ex) { HandleError("Не удалось продолжить ветку истории", ex); }
        finally { IsBusy = false; }
    }
    private async Task ShiftVariantAsync(ChatMessageViewModel? message, int direction)
    {
        if (message is null || SelectedPersonalConversation is null) return;
        var target = message.GetAdjacentVariant(direction);
        if (target is null) return;
        try
        {
            var conversation = await _conversations.SelectPersonalVariantAsync(SelectedPersonalConversation.Id, message.MessageId, target.Id);
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
            message.SelectVariant(target.Id);
            PreviousVariantCommand.RaiseCanExecuteChanged();
            NextVariantCommand.RaiseCanExecuteChanged();
            Status = $"Выбран вариант {message.CurrentVariantNumber} из {message.VariantCount}.";
        }
        catch (Exception ex) { HandleError("Не удалось сменить вариант ответа", ex); }
    }
    private async Task ContinueChatAsync()
    {
        // Continuation deliberately does not create a user turn. The model receives the existing
        // dialogue ending with its last reply and produces the next in-character reply directly.
        var character = SelectedCharacter;
        var conversation = SelectedPersonalConversation;
        if (character is null || conversation is null || IsBusy) return;
        var conversationId = conversation.Id;
        _cognitiveScheduler.Cancel(character.Id, conversationId);
        ChatMessageViewModel? liveAssistant = null;
        try
        {
            IsBusy = true;
            Status = "Модель формирует продолжение…";
            IsAssistantTyping = true;

            var liveVariant = new SoulMessageVariant { Label = "Формируется", Content = "", CreatedAt = DateTimeOffset.Now };
            var liveRecord = new SoulMessage
            {
                Role = SoulMessageRole.Assistant,
                AuthorName = character.Name,
                CurrentVariantId = liveVariant.Id,
                Variants = [liveVariant],
                CreatedAt = DateTimeOffset.Now
            };
            liveAssistant = new ChatMessageViewModel(liveRecord, character.AvatarPath);
            var liveAssistantAdded = false;
            var streamPreview = new StreamingPreviewPublisher(preview =>
            {
                if (!liveAssistantAdded)
                {
                    AddPersonalPresentationMessage(liveAssistant);
                    liveAssistantAdded = true;
                    IsAssistantTyping = false;
                }
                liveVariant.Content = preview;
                liveAssistant.RefreshStreamingPreview();
            });

            var assistantText = await Task.Run(async () =>
            {
                var buffer = new StringBuilder();
                await foreach (var chunk in GenerateAsync(character, conversationId, "*continue*", CancellationToken.None, isContinuation: true).ConfigureAwait(false))
                {
                    buffer.Append(chunk);
                    streamPreview.TryPublish(buffer.ToString());
                }
                return buffer.ToString();
            });

            streamPreview.Stop();
            assistantText = await DirectChatResponseFinalizer.FinalizeAsync(
                _stateVariables, character.Id, conversationId, assistantText, character.UseRoleplayResponseFormatting);
            RefreshStateVariableValues();

            var exactRecentMatch = AssistantReplyCompare.IsExactRecentDuplicate(conversation.Conversation, assistantText);
            AppLog.Write($"CHAT_CONTINUATION_RESPONSE character={character.Id} chat={conversationId} chars={assistantText.Length} hash={AppLog.Fingerprint(assistantText)} exactRecentMatch={exactRecentMatch} preview=«{AppLog.Preview(assistantText)}»");
            var savedAssistant = await _conversations.AppendAssistantMessageAsync(ConversationAddress.Direct(conversationId), assistantText);
            AdoptPersonalConversation(savedAssistant.Conversation);
            var assistant = ConversationMessageMapper.ToPersonalMessage(savedAssistant.Conversation, savedAssistant.Conversation.Messages.Last());
            if (!liveAssistantAdded)
            {
                liveVariant.Content = assistantText;
                AddPersonalPresentationMessage(liveAssistant);
                liveAssistantAdded = true;
            }
            liveAssistant.AdoptPersistedMessage(assistant);
            if (IsHomePage) RebuildHomeCards();
            RebuildConversationItems();
            _ = ScheduleCognitiveMaintenanceAfterReplyAsync(character.Id, conversationId);
            Status = "Готово.";
        }
        catch (Exception ex)
        {
            if (liveAssistant is not null) Messages.Remove(liveAssistant);
            HandleError("Ошибка продолжения диалога", ex);
        }
        finally
        {
            IsAssistantTyping = false;
            IsBusy = false;
        }
    }
    private async Task SendAsync()
    {
        // A new user turn takes priority over any scheduled cognitive maintenance for this dialogue.
        var character = SelectedCharacter;
        var conversation = SelectedPersonalConversation;
        if (character is null || conversation is null || string.IsNullOrWhiteSpace(Draft)) return;
        var conversationId = conversation.Id;
        _cognitiveScheduler.Cancel(character.Id, conversationId);
        var text = Draft.Trim();
        Draft = "";
        ChatMessageViewModel? liveAssistant = null;
        try
        {
            IsBusy = true;
            var author = ComposerAuthor;
            var savedUser = await _conversations.AppendAuthoredUserMessageAsync(
                ConversationAddress.Direct(conversationId), text, author.Kind, author.PersonaId,
                author.DisplayName, author.AvatarPath);
            AdoptPersonalConversation(savedUser.Conversation);
            var user = ConversationMessageMapper.ToPersonalMessage(savedUser.Conversation, savedUser.Conversation.Messages.Last());
            var displayedUser = new ChatMessageViewModel(user, character.AvatarPath);
            AddPersonalPresentationMessage(displayedUser);

            if (character.RealisticMessagingEnabled)
            {
                var delay = MessagingTiming.RealisticReplyDelay(text);
                Status = $"{character.Name} ответит примерно через {Math.Ceiling(delay.TotalSeconds):0} сек.";
                await Task.Delay(delay);
            }

            Status = "Модель формирует ответ…";
            IsAssistantTyping = true;

                        // The SSE reader can receive hundreds of small chunks per answer. Keep parsing and
            // string accumulation off the WPF dispatcher, then refresh the visual preview at most
            // roughly 12 times per second. This prevents generation from starving mouse, scrolling,
            // window movement and repainting on the UI thread.
            var liveVariant = new SoulMessageVariant { Label = "Формируется", Content = "", CreatedAt = DateTimeOffset.Now };
            var liveRecord = new SoulMessage
            {
                Role = SoulMessageRole.Assistant,
                AuthorName = character.Name,
                CurrentVariantId = liveVariant.Id,
                Variants = [liveVariant],
                CreatedAt = DateTimeOffset.Now
            };
            liveAssistant = new ChatMessageViewModel(liveRecord, character.AvatarPath);
            var liveAssistantAdded = false;
            var streamPreview = new StreamingPreviewPublisher(preview =>
            {
                if (!liveAssistantAdded)
                {
                    AddPersonalPresentationMessage(liveAssistant);
                    liveAssistantAdded = true;
                    IsAssistantTyping = false;
                }
                liveVariant.Content = preview;
                liveAssistant.RefreshStreamingPreview();
            });

            var assistantText = await Task.Run(async () =>
            {
                var buffer = new StringBuilder();
                await foreach (var chunk in GenerateAsync(character, conversationId, text, CancellationToken.None).ConfigureAwait(false))
                {
                    buffer.Append(chunk);
                    streamPreview.TryPublish(buffer.ToString());
                }
                return buffer.ToString();
            });

            streamPreview.Stop();
            assistantText = await DirectChatResponseFinalizer.FinalizeAsync(
                _stateVariables, character.Id, conversationId, assistantText, character.UseRoleplayResponseFormatting);
            RefreshStateVariableValues();

            var exactRecentMatch = AssistantReplyCompare.IsExactRecentDuplicate(savedUser.Conversation, assistantText);
            AppLog.Write($"CHAT_RESPONSE character={character.Id} chat={conversationId} chars={assistantText.Length} hash={AppLog.Fingerprint(assistantText)} exactRecentMatch={exactRecentMatch} preview=«{AppLog.Preview(assistantText)}»");
            var savedAssistant = await _conversations.AppendAssistantMessageAsync(ConversationAddress.Direct(conversationId), assistantText);
            AdoptPersonalConversation(savedAssistant.Conversation);
            var assistant = ConversationMessageMapper.ToPersonalMessage(savedAssistant.Conversation, savedAssistant.Conversation.Messages.Last());
            // Оставляем тот же визуальный элемент, который показывал потоковый текст. Раньше он
            // удалялся, а затем создавался заново, из-за чего лента мигала и прыгала в конце ответа.
            if (!liveAssistantAdded)
            {
                liveVariant.Content = assistantText;
                AddPersonalPresentationMessage(liveAssistant);
                liveAssistantAdded = true;
            }
            liveAssistant.AdoptPersistedMessage(assistant);
            if (IsHomePage) RebuildHomeCards();
            RebuildConversationItems();
            _ = ScheduleCognitiveMaintenanceAfterReplyAsync(character.Id, conversationId);
            Status = "Готово.";
        }
        catch (Exception ex)
        {
            if (liveAssistant is not null) Messages.Remove(liveAssistant);
            HandleError("Ошибка диалога", ex);
        }
        finally
        {
            IsAssistantTyping = false;
            IsBusy = false;
        }
    }
    private void SetChatAppearanceColor(string? value)
    {
        if (!ChatAppearanceEditor.TryApplyColorToken(ChatAppearance, value)) return;
    }

    private void ChatAppearance_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        => _ = SaveChatAppearanceAsync();
    private async Task SaveChatAppearanceAsync()
    {
        try
        {
            await _store.MutateAsync(root => root.Preferences.ChatAppearance = ChatAppearance.Clone(), "save_chat_appearance");
            Status = "Оформление чата сохранено рядом с программой.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить оформление чата", ex); }
    }
    private void ResetChatAppearance()
    {
        ChatAppearance = new ChatAppearanceSettings();
        _ = SaveChatAppearanceAsync();
        Status = "Оформление сброшено к стандартному виду и сохранено автоматически.";
    }
}
