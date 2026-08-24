using System.Windows;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<SoulCharacter> GenerateCharacterFromNetworkAsync(string idea, CancellationToken token)
    {
        SoulCharacter? result = null;
        async Task RunAsync()
        {
            var before = Characters.Select(character => character.Id).ToHashSet();
            CharacterGenerationIdea = idea;
            await GenerateCharacterFromIdeaAsync();
            result = Characters.FirstOrDefault(character => !before.Contains(character.Id));
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) await RunAsync();
        else await dispatcher.InvokeAsync(RunAsync).Task.Unwrap();
        return result ?? throw new InvalidOperationException("Не удалось сгенерировать карточку персонажа.");
    }
    private async Task<SoulCharacter> ExpandCharacterFieldFromNetworkAsync(Guid characterId, string field, CancellationToken token)
    {
        SoulCharacter? result = null;
        async Task RunAsync()
        {
            var target = Characters.FirstOrDefault(character => character.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
            SelectedCharacter = target;
            await ExpandCharacterFieldAsync(field);
            result = SelectedCharacter;
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) await RunAsync();
        else await dispatcher.InvokeAsync(RunAsync).Task.Unwrap();
        return result ?? throw new InvalidOperationException("Не удалось дополнить поле персонажа.");
    }
    private async Task ControlSceneFromNetworkAsync(Guid sceneId, string action, CancellationToken token)
    {
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedAction)
        {
            case "start":
                await _conversations.SetSceneStatusAsync(new ConversationAddress(sceneId, ConversationKind.Scene), ConversationSceneStatusAction.Start, token);
                await ScheduleAutomaticSceneTurnAsync(sceneId, token);
                return;
            case "pause":
                _sceneTurnScheduler.Cancel(sceneId);
                await _conversations.SetSceneStatusAsync(new ConversationAddress(sceneId, ConversationKind.Scene), ConversationSceneStatusAction.Pause, token);
                return;
            case "finish":
                _sceneTurnScheduler.Cancel(sceneId);
                await _conversations.SetSceneStatusAsync(new ConversationAddress(sceneId, ConversationKind.Scene), ConversationSceneStatusAction.Finish, token);
                return;
            case "next":
                _sceneTurnScheduler.Cancel(sceneId);
                await GenerateNetworkSceneTurnAsync(sceneId, token);
                await ScheduleAutomaticSceneTurnAsync(sceneId, token);
                return;
            default:
                throw new InvalidOperationException("Неизвестное действие сцены.");
        }
    }
    private Task RefreshDesktopAfterNetworkMutationAsync()
    {
        if (Interlocked.Exchange(ref _networkRefreshQueued, 1) != 0) return Task.CompletedTask;
        _ = Task.Run(async () =>
        {
            try
            {
                // Group a burst of mobile writes (for example a sent message and its reply) into one UI refresh.
                await Task.Delay(250).ConfigureAwait(false);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                await dispatcher.InvokeAsync(async () =>
                {
                    var characterId = SelectedCharacter?.Id;
                    var chatId = SelectedPersonalConversation?.Id;
                    var sceneId = SelectedGroupConversation?.Id;
                    await ReloadCharactersAsync(characterId);
                    if (chatId is not null)
                    {
                        var restored = _conversationSnapshots.FirstOrDefault(conversation => conversation.Id == chatId.Value && conversation.Mode == ConversationMode.Personal);
                        if (restored is not null)
                        {
                            SelectedPersonalConversation = new PersonalConversationEditorViewModel(restored);
                            LoadMessages();
                            RefreshStateVariableValues();
                        }
                    }
                    await ReloadScenesAsync(sceneId);
                    Status = "Данные синхронизированы с SoulExe Mobile.";
                }).Task.Unwrap().ConfigureAwait(false);
            }
            catch (Exception ex) { AppLog.Write($"Не удалось обновить интерфейс после мобильного изменения: {ex}"); }
            finally { Interlocked.Exchange(ref _networkRefreshQueued, 0); }
        });
        return Task.CompletedTask;
    }
    private async Task<string> AskFromNetworkAsync(NetworkChatRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(request.CharacterId, out var id)) throw new InvalidOperationException("Некорректный персонаж.");
        var character = await _library.GetCharacterAsync(id) ?? throw new InvalidOperationException("Персонаж не найден.");
        var personalConversations = (await _conversations.GetAllAsync(token))
            .Where(conversation => conversation.Mode == ConversationMode.Personal && conversation.Participants.Any(participant => participant.CharacterId == character.Id)).ToList();
        if (personalConversations.Count == 0) throw new InvalidOperationException("У выбранного персонажа пока нет разговоров.");
        var conversation = Guid.TryParse(request.ChatId, out var selectedChatId)
            ? personalConversations.FirstOrDefault(value => value.Id == selectedChatId)
            : null;
        conversation ??= personalConversations.FirstOrDefault(value => value.Id == character.CurrentChatId) ?? personalConversations.First();
        // A mobile turn must have the same priority as a desktop turn. Without this,
        // an already running memory task can occupy the local model while the phone waits.
        _cognitiveScheduler.Cancel(character.Id, conversation.Id);
        var authorKind = (request.AuthorKind ?? "user").Trim().ToLowerInvariant();
        Guid? personaId = null;
        SoulPersona? persona = null;
        var storedAuthorKind = SoulMessageAuthorKind.User;
        var authorName = "Вы";
        var avatarPath = "";
        if (authorKind == "director")
        {
            storedAuthorKind = SoulMessageAuthorKind.Director;
            authorName = "Режиссёр";
        }
        else if (authorKind == "persona")
        {
            if (!Guid.TryParse(request.AuthorPersonaId, out var parsedPersonaId)) throw new InvalidOperationException("Выберите персону для сообщения.");
            persona = (await _personas.GetPersonasAsync(token)).FirstOrDefault(value => value.Id == parsedPersonaId)
                ?? throw new InvalidOperationException("Персона не найдена.");
            personaId = persona.Id;
            storedAuthorKind = SoulMessageAuthorKind.Persona;
            authorName = persona.Name;
            avatarPath = persona.AvatarPath;
        }
        else if (authorKind != "user") throw new InvalidOperationException("Неизвестный автор сообщения.");
        await _conversations.AppendAuthoredUserMessageAsync(ConversationAddress.Direct(conversation.Id), request.Message, storedAuthorKind, personaId, authorName, avatarPath, token);
        var settings = await BuildLlamaSettingsAsync();
        var generationId = Guid.NewGuid().ToString("N")[..12];
        var result = await _conversationTurnRunner.RunPersonalTurnAsync(
            character.Id,
            conversation.Id,
            request.Message,
            isContinuation: false,
            settings.ContextSize,
            settings.MaxTokens,
            (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, "network_" + generationId),
            async (raw, cancellation) => await DirectChatResponseFinalizer.FinalizeAsync(
                _stateVariables, character.Id, conversation.Id, raw, character.UseRoleplayResponseFormatting, cancellation),
            persistAssistant: true,
            activePersonaId: personaId,
            token: token);
        if (result.Status == DirectTurnExecutionStatus.AlreadyRunning)
            throw new InvalidOperationException("Для этого чата уже формируется ответ.");
        AppLog.Write($"NETWORK_CHAT_RESPONSE_FORMATTED character={character.Id:N} chat={conversation.Id:N} enabled={character.UseRoleplayResponseFormatting} chars={result.Content.Length}");
        await ScheduleCognitiveMaintenanceAsync(character.Id, conversation.Id);
        return result.Content;
    }
}
