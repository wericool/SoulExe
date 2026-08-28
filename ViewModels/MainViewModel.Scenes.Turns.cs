using System.Windows;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private async Task GenerateNetworkSceneTurnAsync(Guid sceneId, CancellationToken token)
    {
        try
        {
            var settings = await BuildLlamaSettingsAsync();
            var result = await _conversationTurnRunner.RunGroupTurnAsync(
                sceneId,
                settings.ContextSize,
                settings.MaxTokens,
                (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, "network_scene_" + Guid.NewGuid().ToString("N")[..8]),
                SceneReplyNormalizer.Create(_stateVariables),
                started => AppLog.Write($"NETWORK_SCENE_BEGIN scene={started.SceneId:N} speaker={started.SpeakerCharacterId:N}"),
                token: token);
            if (result.Status != SceneTurnExecutionStatus.Completed) return;
            var snapshot = await _conversations.GetGroupAsync(sceneId, token);
            var firstCharacterId = snapshot?.Participants
                .Where(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId is not null)
                .OrderBy(value => value.SortOrder)
                .Select(value => value.CharacterId)
                .FirstOrDefault();
            if (firstCharacterId is Guid characterId) ScheduleSceneSummary(characterId, sceneId);
            AppLog.Write($"NETWORK_SCENE_SAVED scene={sceneId:N} message={result.SavedMessage?.Id:N} chars={result.Content.Length} status={result.NextStatus}");
        }
        catch (Exception ex) when (IsContextCapacityError(ex))
        {
            await PauseSceneAfterContextCapacityErrorAsync(sceneId, token);
            AppLog.Write($"NETWORK_SCENE_PAUSED_CONTEXT_LIMIT scene={sceneId:N}: {ex.Message}");
            throw new InvalidOperationException("Контекст группового разговора достиг лимита модели. Разговор поставлен на паузу; повторите ход после сокращения истории.", ex);
        }
    }
    private Task ScheduleAutomaticSceneTurnAsync(Guid sceneId, CancellationToken token = default) =>
        _sceneTurnScheduler.ScheduleAsync(sceneId, GenerateScheduledSceneTurnAsync, token);
    private async Task GenerateScheduledSceneTurnAsync(Guid sceneId, CancellationToken token)
    {
        if (SelectedGroupConversation?.Id == sceneId && Application.Current?.Dispatcher is { } dispatcher && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
        {
            await dispatcher.InvokeAsync(async () => await GenerateNextSceneTurnAsync()).Task.Unwrap();
            return;
        }
        await GenerateNetworkSceneTurnAsync(sceneId, token);
    }
    private async Task GenerateNextSceneTurnAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null || editor.CharacterIds.Count < 2 || IsSceneGenerating || IsBusy) return;
        var conversationId = editor.Id;
        var firstCharacterId = editor.CharacterIds[0];
        // A one-off turn from the paused state must stay one-off. The runner
        // advances the next speaker, but its persisted default status is Running.
        var continueAutomatically = string.Equals(editor.Status, SceneStatus.Running, StringComparison.OrdinalIgnoreCase);
        try
        {
            CancelSceneTimer();
            _sceneTurnScheduler.Cancel(conversationId);
            _cognitiveScheduler.Cancel(firstCharacterId, conversationId);
            IsSceneGenerating = true;
            await _conversations.UpdateGroupAsync(editor.Id, [editor.CharacterIds[0], editor.CharacterIds[1]], editor.Name,
                editor.Scenario, editor.Location, editor.TimeContext, editor.Mood, editor.Goal, editor.RelationshipContext,
                editor.TurnMode, editor.DelaySeconds, editor.EnforceConversationContract, editor.AdvanceAndAvoidRepetition);
            var settings = await BuildLlamaSettingsAsync();
            SceneMessageViewModel? live = null;
            var liveAdded = false;
            var previewActive = 1;
            var lastPreviewAt = 0L;
            var dispatcher = Application.Current?.Dispatcher;
            await UpdateSceneUiAsync(() => { IsSceneTyping = true; });

            void PublishScenePreview(string preview)
            {
                if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    if (Volatile.Read(ref previewActive) == 0) return;
                    if (!liveAdded && live is not null)
                    {
                        AddScenePresentationMessage(live);
                        liveAdded = true;
                        IsSceneTyping = false;
                    }
                    live?.SetContent(preview);
                }));
            }

            var result = await Task.Run(async () => await _conversationTurnRunner.RunGroupTurnAsync(
                conversationId,
                settings.ContextSize,
                settings.MaxTokens,
                (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, "scene_" + Guid.NewGuid().ToString("N")[..8]),
                SceneReplyNormalizer.Create(_stateVariables),
                started =>
                {
                    live = SceneMessageViewModel.Live(started.Speaker.Name, started.SpeakerCharacterId == firstCharacterId, started.Speaker.AvatarPath);
                    if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                        _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => SceneRunStatus = $"{started.Speaker.Name} формирует реплику…"));
                },
                preview =>
                {
                    var now = Environment.TickCount64;
                    if (now - Interlocked.Read(ref lastPreviewAt) >= 85)
                    {
                        Interlocked.Exchange(ref lastPreviewAt, now);
                        PublishScenePreview(preview);
                    }
                }));

            if (result.Status == SceneTurnExecutionStatus.AlreadyRunning)
            {
                SceneRunStatus = "Для этого группового разговора уже формируется реплика.";
                return;
            }
            if (result.Status == SceneTurnExecutionStatus.Finished) return;

            Interlocked.Exchange(ref previewActive, 0);
            var text = result.Content;

            // The streaming bubble remains in place. Removing it and rebuilding the full list caused a visible flash/jump.
            await UpdateSceneUiAsync(() =>
            {
                IsSceneTyping = false;
                if (!liveAdded && live is not null)
                {
                    AddScenePresentationMessage(live);
                    liveAdded = true;
                }
                live?.SetContent(text);
            });

            _ = result.SavedMessage ?? throw new InvalidOperationException("Общий runner не вернул сохранённую реплику группового разговора.");
            if (!continueAutomatically)
                await _conversations.SetSceneStatusAsync(ConversationAddress.Scene(conversationId), ConversationSceneStatusAction.Pause);
            var refreshed = await _conversations.GetGroupAsync(conversationId)
                ?? throw new InvalidOperationException("Групповой разговор не найден после сохранения реплики.");

            // Keep the selected scene in sync without recreating SceneMessages and without replacing the live bubble.
            await UpdateSceneUiAsync(() =>
            {
                if (SelectedGroupConversation?.Id == conversationId)
                {
                    SelectedGroupConversation = new GroupConversationEditorViewModel(refreshed);
                    OnPropertyChanged(nameof(SceneNextSpeakerName));
                    OnPropertyChanged(nameof(SceneStartPauseText));
                    OnPropertyChanged(nameof(SceneStartPauseIcon));
                    OnPropertyChanged(nameof(IsSceneFinished));
                    OnPropertyChanged(nameof(SceneLastMessageLabel));
                    RefreshSceneMessageSearchResults();
                    RebuildConversationItems();
                    RaiseSceneCommands();
                }
            });

            ScheduleSceneSummary(firstCharacterId, conversationId);
            SceneRunStatus = $"{result.SpeakerName} ответил. Общий Summary при необходимости обновится в фоне после короткой паузы.";
            if (continueAutomatically)
            {
                if (SelectedGroupConversation?.Status == SceneStatus.Running && SelectedGroupConversation.DelaySeconds >= 5) ScheduleSceneTimer();
                await ScheduleAutomaticSceneTurnAsync(conversationId);
            }
        }
        catch (Exception ex)
        {
            if (IsContextCapacityError(ex))
            {
                await PauseSceneAfterContextCapacityErrorAsync(conversationId);
                SceneRunStatus = "Контекст группового разговора достиг лимита модели. Разговор поставлен на паузу; следующий ход после обновления SoulExe автоматически сократит старую историю.";
            }
            HandleError("Не удалось выполнить ход группового разговора", ex);
        }
        finally
        {
            await UpdateSceneUiAsync(() =>
            {
                IsSceneTyping = false;
                IsSceneGenerating = false;
            });
        }
    }
    private void ScheduleSceneSummary(Guid characterId, Guid sceneId, bool immediate = false)
    {
        _cognitiveScheduler.Schedule(characterId, sceneId, immediate ? "immediate" : "delayed", 10, async token =>
        {
            var summary = await _conversations.UpdateGroupSummaryAsync(sceneId, CompleteSceneSummaryAsync, false, 6, token);
            if (!summary.Updated) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                await dispatcher.InvokeAsync(async () =>
                {
                    if (SelectedGroupConversation?.Id != sceneId) return;
                    await LoadSelectedSceneAsync(sceneId);
                    SceneRunStatus = "Общий Summary группового разговора обновлён в фоне.";
                }).Task.Unwrap();
            }
        });
    }
    private async Task<string> CompleteSceneSummaryAsync(IReadOnlyList<LlamaMessage> messages, CancellationToken token)
    {
        var settings = LlamaSettingsFactory.ForSceneSummary(await BuildLlamaSettingsAsync());
        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, messages, token, "scene_summary_" + Guid.NewGuid().ToString("N")[..8])) answer.Append(chunk);
        return answer.ToString();
    }
    private void ScheduleSceneTimer()
    {
        if (SelectedGroupConversation is null || SelectedGroupConversation.Status != SceneStatus.Running || SelectedGroupConversation.DelaySeconds < 5) return;
        CancelSceneTimer();
        var sceneId = SelectedGroupConversation.Id;
        var delay = SelectedGroupConversation.DelaySeconds;
        var source = _sceneTimerCts = new CancellationTokenSource();
        SceneCountdownSeconds = delay;
        SceneRunStatus = $"Следующая реплика {SceneNextSpeakerName} через {delay} сек. Нажмите «Пауза», чтобы остановить таймер.";
        _ = WaitSceneTurnAsync(sceneId, delay, source.Token);
    }
    private async Task WaitSceneTurnAsync(Guid sceneId, int delaySeconds, CancellationToken token)
    {
        try
        {
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                if (token.IsCancellationRequested) return;
                var isCurrentScene = false;
                await UpdateSceneUiAsync(() =>
                {
                    isCurrentScene = SelectedGroupConversation?.Id == sceneId && SelectedGroupConversation?.Status == SceneStatus.Running;
                    if (isCurrentScene)
                    {
                        SceneCountdownSeconds = remaining;
                        SceneRunStatus = $"Следующая реплика {SceneNextSpeakerName} через {remaining} сек. Нажмите «Пауза», чтобы остановить таймер.";
                    }
                });
                if (!isCurrentScene) return;
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }

            // SceneTurnScheduler owns actual generation. This local timer only keeps the header
            // countdown responsive while the persisted NextTurnAt remains the source of truth.
        }
        catch (OperationCanceledException) { }
    }
    private void CancelSceneTimer()
    {
        _sceneTimerCts?.Cancel();
        _sceneTimerCts?.Dispose();
        _sceneTimerCts = null;
        SceneCountdownSeconds = 0;
    }
    private void CloseRenameSceneDialog()
    {
        IsRenameSceneDialogOpen = false;
        RenameScene = null;
        RenameSceneNameDraft = "";
    }
    private async Task PauseSceneAfterContextCapacityErrorAsync(Guid sceneId, CancellationToken token = default)
    {
        CancelSceneTimer();
        _sceneTurnScheduler.Cancel(sceneId);
        try { await _conversations.SetSceneStatusAsync(ConversationAddress.Scene(sceneId), ConversationSceneStatusAction.Pause, token); }
        catch (Exception pauseException) { AppLog.Write($"Не удалось поставить сцену {sceneId:N} на паузу после превышения контекста.", pauseException); }

        await UpdateSceneUiAsync(() =>
        {
            if (SelectedGroupConversation?.Id != sceneId) return;
            if (SelectedGroupConversation.Conversation.TurnState is { } turn) turn.Status = SceneStatus.Paused;
            OnPropertyChanged(nameof(SelectedGroupConversation));
            OnPropertyChanged(nameof(SceneStartPauseText));
            OnPropertyChanged(nameof(SceneStartPauseIcon));
            RaiseSceneCommands();
        });
    }
}
