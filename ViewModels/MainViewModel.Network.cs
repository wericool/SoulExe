using System.Text;
using System.Text.Json;
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

    private async Task<SoulPersona> GeneratePersonaFromNetworkAsync(string idea, CancellationToken token)
    {
        SoulPersona? result = null;
        async Task RunAsync()
        {
            var russianInput = idea.Any(character => (character >= 'А' && character <= 'я') || character is 'Ё' or 'ё');
            var languageLock = russianInput ? "Write every value in natural Russian." : "Write every value in the same language as the user's idea.";
            var messages = new[]
            {
                new LlamaMessage("system", $"You generate a user persona for roleplay chats. Return strict JSON only. {languageLock}"),
                new LlamaMessage("user", $"""
Create a concise reusable user persona from this idea. {languageLock}
Return strict JSON only, with string fields: name, description, promptText.
description must be a warm, concrete self-description of 180 to 300 characters. promptText must be 180 to 300 characters and tell the chat character how to address and treat this person; do not include meta commentary or <think> tags.

User's idea:
{idea}
""")
            };
            Status = "Локальная модель создаёт персону…";
            var settings = await BuildLlamaSettingsAsync();
            var raw = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, messages, token, "persona_generator").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            }, token);
            var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty, StringComparison.Ordinal).Trim();
            var start = text.IndexOf('{'); var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) throw new InvalidOperationException("Модель вернула персону в непонятном формате. Попробуйте уточнить описание.");
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            static string Value(JsonElement source, string name) => source.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? "" : "";
            var root = document.RootElement;
            var name = Value(root, "name");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Модель не указала имя персоны. Попробуйте ещё раз.");
            result = await _personas.CreateAsync(name, token);
            result.Description = CharacterCardGenerationService.LimitField(Value(root, "description"), 500);
            result.PromptText = CharacterCardGenerationService.LimitField(Value(root, "promptText"), 500);
            await _personas.UpdateAsync(result, token);
            await ReloadPersonasAsync(result.Id);
            Status = $"Персона «{result.Name}» создана локальной моделью.";
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) await RunAsync();
        else await dispatcher.InvokeAsync(RunAsync).Task.Unwrap();
        return result ?? throw new InvalidOperationException("Не удалось сгенерировать персону.");
    }

    private async Task<string> ExpandPersonaDescriptionFromNetworkAsync(string idea, CancellationToken token)
    {
        string? result = null;
        async Task RunAsync()
        {
            var messages = new[]
            {
                new LlamaMessage("system", "You expand a concise user-persona description for a roleplay chat. Return only the expanded Russian description, without a heading, JSON, Markdown, quotes, or <think> tags."),
                new LlamaMessage("user", $"""
Expand the following Russian persona description into a concrete, internally consistent text of 180 to 300 characters.
Keep every fact that is explicitly given. Add only plausible stable details that follow from this text. Do not use, infer, or mention the persona's name. Do not invent dialogue or actions by other people.

Source description:
{idea.Trim()}
""")
            };
            Status = "Локальная модель пишет описание персоны…";
            var settings = await BuildLlamaSettingsAsync();
            var raw = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, messages, token, "persona_description_network").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            }, token);
            result = CharacterCardGenerationService.LimitField(raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(), 500);
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Модель не вернула описание. Попробуйте ещё раз.");
            Status = "Описание персоны сгенерировано в мобильном редакторе.";
        }
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) await RunAsync();
        else await dispatcher.InvokeAsync(RunAsync).Task.Unwrap();
        return result ?? throw new InvalidOperationException("Не удалось сгенерировать описание персоны.");
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
                // "Следующая реплика" is a one-off manual turn.  The runner
                // temporarily marks the scene running while generating, so put
                // it back on pause rather than starting an automatic cycle.
                await _conversations.SetSceneStatusAsync(new ConversationAddress(sceneId, ConversationKind.Scene), ConversationSceneStatusAction.Pause, token);
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
    private Task<string> AskFromNetworkAsync(NetworkChatRequest request, CancellationToken token) =>
        AskFromNetworkCoreAsync(request, null, token);

    private Task<string> AskFromNetworkWithPreviewAsync(NetworkChatRequest request, Action<string> onChunk, CancellationToken token) =>
        AskFromNetworkCoreAsync(request, onChunk, token);

    private async Task<string> AskFromNetworkCoreAsync(NetworkChatRequest request, Action<string>? onChunk, CancellationToken token)
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
        var isContinuation = string.Equals(request.Message.Trim(), "*continue*", StringComparison.OrdinalIgnoreCase);
        if (!isContinuation)
            await _conversations.AppendAuthoredUserMessageAsync(ConversationAddress.Direct(conversation.Id), request.Message, storedAuthorKind, personaId, authorName, avatarPath, token);
        await RefreshDesktopAfterNetworkMutationAsync();
        if (!isContinuation && character.RealisticMessagingEnabled)
            await Task.Delay(MessagingTiming.RealisticReplyDelay(request.Message), token);
        var settings = await BuildLlamaSettingsAsync();
        var generationId = Guid.NewGuid().ToString("N")[..12];
        var result = await _conversationTurnRunner.RunPersonalTurnAsync(
            character.Id,
            conversation.Id,
            request.Message,
            isContinuation: isContinuation,
            settings.ContextSize,
            settings.MaxTokens,
            (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, "network_" + generationId),
            async (raw, cancellation) => await DirectChatResponseFinalizer.FinalizeAsync(
                _stateVariables, character.Id, conversation.Id, raw, character.UseRoleplayResponseFormatting, cancellation),
            onChunk: onChunk,
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
