using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private void EnsureSceneDraftParticipants()
    {
        var (a, b) = SceneParticipantPicker.EnsureDefaults(Characters, SceneCharacterA, SceneCharacterB);
        if (!ReferenceEquals(SceneCharacterA, a)) SceneCharacterA = a;
        if (!ReferenceEquals(SceneCharacterB, b)) SceneCharacterB = b;
    }
    private async Task ReloadScenesAsync(Guid? selectId = null)
    {
        var currentId = selectId ?? SelectedGroupConversation?.Id;
        _conversationSnapshots = await _conversations.GetAllAsync().ConfigureAwait(false);
        var groups = _conversationSnapshots.Where(value => value.Mode == ConversationMode.Group).ToList();
        var targetId = currentId is not null && groups.Any(value => value.Id == currentId)
            ? currentId
            : groups.FirstOrDefault()?.Id;
        await LoadSelectedSceneAsync(targetId);
        RebuildConversationItems();
    }
    private async Task LoadSelectedSceneAsync(Guid? sceneId)
    {
        var snapshot = sceneId is null ? null : await _conversations.GetGroupAsync(sceneId.Value).ConfigureAwait(false);
        await UpdateSceneUiAsync(() =>
        {
            SceneMessages.Clear();
            if (snapshot is null)
            {
                SelectedGroupConversation = null;
                foreach (var name in SceneUiNotifications.SelectionWithoutLastMessage) OnPropertyChanged(name);
                RaiseSceneCommands();
                return;
            }

            SelectedGroupConversation = new GroupConversationEditorViewModel(snapshot!);
            foreach (var view in SceneMessageTimeline.Build(snapshot!, Characters))
                SceneMessages.Add(view);
            foreach (var name in SceneUiNotifications.SelectionChanged) OnPropertyChanged(name);
            RefreshSceneMessageSearchResults();
            RaiseSceneCommands();
        });
    }
    private void BeginCreateScene()
    {
        IsSceneComposerOpen = true;
        SceneRunStatus = "Заполните участников и условия нового группового разговора.";
    }
    private async Task CreateSceneAsync()
    {
        if (SceneCharacterA is null || SceneCharacterB is null || SceneCharacterA.Id == SceneCharacterB.Id) return;
        try
        {
            IsBusy = true;
            var created = await _conversations.CreateAsync([SceneCharacterA.Id, SceneCharacterB.Id], SceneNameDraft, SceneScenarioDraft, SceneLocationDraft, SceneTimeDraft, SceneMoodDraft, SceneGoalDraft, SceneRelationshipDraft, SceneTurnModeDraft, SceneDelaySecondsDraft, SceneEnforceContractDraft, SceneAdvanceNarrativeDraft);
            var conversationId = created.Conversation.Id;
            SceneNameDraft = SceneDraftDefaults.Name; SceneScenarioDraft = SceneDraftDefaults.Scenario; SceneLocationDraft = SceneDraftDefaults.Location; SceneTimeDraft = SceneDraftDefaults.Time; SceneMoodDraft = SceneDraftDefaults.Mood; SceneGoalDraft = SceneDraftDefaults.Goal; SceneRelationshipDraft = ""; SceneAdvanceNarrativeDraft = true;
            IsSceneComposerOpen = false;
            await ReloadScenesAsync(conversationId);
            var listItem = ConversationItems.FirstOrDefault(item => item.IsScene && item.Id == conversationId);
            if (listItem is not null) SelectedConversationItem = listItem;
            SceneRunStatus = "Групповой разговор создан. Нажмите «Старт» или сделайте следующий ход вручную.";
        }
        catch (Exception ex) { HandleError("Не удалось создать групповой разговор", ex); }
        finally { IsBusy = false; }
    }
    private async Task SaveSceneAsync()
    {
        if (SelectedGroupConversation is null) return;
        try
        {
            var editor = SelectedGroupConversation;
            var sceneId = editor.Id;
            var ids = editor.CharacterIds;
            if (ids.Count < 2) throw new InvalidOperationException("Групповой разговор должен содержать двух персонажей.");
            await _conversations.UpdateGroupAsync(editor.Id, [ids[0], ids[1]], editor.Name, editor.Scenario, editor.Location,
                editor.TimeContext, editor.Mood, editor.Goal, editor.RelationshipContext, editor.TurnMode, editor.DelaySeconds,
                editor.EnforceConversationContract, editor.AdvanceAndAvoidRepetition);
            await ReloadScenesAsync(sceneId);
            if (SelectedGroupConversation?.Status == SceneStatus.Running && SelectedGroupConversation.TurnMode == "alternate" && SelectedGroupConversation.DelaySeconds >= 5)
                ScheduleSceneTimer();
            else
                CancelSceneTimer();
            await ScheduleAutomaticSceneTurnAsync(sceneId);
            SceneRunStatus = "Параметры группового разговора сохранены.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить групповой разговор", ex); }
    }
    private async Task DeleteSceneAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null) return;
        try
        {
            CancelSceneTimer();
            _sceneTurnScheduler.Cancel(editor.Id);
            await _conversations.DeleteAsync(ConversationAddress.Scene(editor.Id));
            await ReloadScenesAsync();
            SceneRunStatus = "Групповой разговор удалён.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить групповой разговор", ex); }
    }

    private async Task ToggleSceneStartPauseAsync()
    {
        if (SelectedGroupConversation is null || SelectedGroupConversation.Status == SceneStatus.Finished) return;
        if (SelectedGroupConversation.Status == SceneStatus.Running) await PauseSceneAsync();
        else await StartSceneAsync();
    }
    private async Task StartSceneAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null) return;
        await _conversations.SetSceneStatusAsync(ConversationAddress.Scene(editor.Id), ConversationSceneStatusAction.Start);
        await LoadSelectedSceneAsync(editor.Id);
        SceneRunStatus = $"Групповой разговор запущен. Следующий ход: {SceneNextSpeakerName}.";
        if (SelectedGroupConversation?.DelaySeconds >= 5 && SelectedGroupConversation.TurnMode == "alternate") ScheduleSceneTimer();
        await ScheduleAutomaticSceneTurnAsync(editor.Id);
    }
    private async Task PauseSceneAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null) return;
        CancelSceneTimer();
        _sceneTurnScheduler.Cancel(editor.Id);
        await _conversations.SetSceneStatusAsync(ConversationAddress.Scene(editor.Id), ConversationSceneStatusAction.Pause);
        await LoadSelectedSceneAsync(editor.Id);
        if (editor.CharacterIds.FirstOrDefault() is var characterId && characterId != Guid.Empty)
            ScheduleSceneSummary(characterId, editor.Id, immediate: true);
        SceneRunStatus = "Групповой разговор поставлен на паузу. История и контекст сохранены; Summary обновится в фоне при необходимости.";
    }
    private async Task FinishSceneAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null) return;
        CancelSceneTimer();
        _sceneTurnScheduler.Cancel(editor.Id);
        await _conversations.SetSceneStatusAsync(ConversationAddress.Scene(editor.Id), ConversationSceneStatusAction.Finish);
        await LoadSelectedSceneAsync(editor.Id);
        if (editor.CharacterIds.FirstOrDefault() is var characterId && characterId != Guid.Empty)
            ScheduleSceneSummary(characterId, editor.Id, immediate: true);
        SceneRunStatus = "Групповой разговор завершён. Личные разговоры персонажей не изменялись; Summary обновится в фоне при необходимости.";
    }
    private async Task ChooseSceneSpeakerAsync(SoulCharacter? character)
    {
        var editor = SelectedGroupConversation;
        if (editor is null || character is null || !editor.CharacterIds.Contains(character.Id)) return;
        CancelSceneTimer();
        _sceneTurnScheduler.Cancel(editor.Id);
        await _conversations.ChooseSceneNextParticipantAsync(ConversationAddress.Scene(editor.Id), character.Id);
        await LoadSelectedSceneAsync(editor.Id);
        SceneRunStatus = $"Следующий ход вручную назначен персонажу {character.Name}.";
    }
    private async Task SendGroupMessageAsync()
    {
        var editor = SelectedGroupConversation;
        if (editor is null || string.IsNullOrWhiteSpace(GroupDraft)) return;
        try
        {
            var author = ComposerAuthor;
            if (author.Kind == SoulMessageAuthorKind.Director)
                await _conversations.AddDirectorEventAsync(ConversationAddress.Scene(editor.Id), GroupDraft);
            else
                await _conversations.AddSceneUserMessageAsync(ConversationAddress.Scene(editor.Id), GroupDraft, author.PersonaId);
            GroupDraft = "";
            await LoadSelectedSceneAsync(editor.Id);
            SceneRunStatus = author.Kind == SoulMessageAuthorKind.Director
                ? "Режиссёрское событие добавлено в общий контекст группового разговора."
                : $"Реплика от имени «{author.DisplayName}» добавлена в групповой разговор.";
        }
        catch (Exception ex) { HandleError("Не удалось добавить режиссёрское событие", ex); }
    }
    private Task UpdateSceneUiAsync(Action update) => UiThread.InvokeAsync(update);
    private void RaiseSceneCommands()
    {
        CreateSceneCommand?.RaiseCanExecuteChanged(); SaveSceneCommand?.RaiseCanExecuteChanged(); DeleteSceneCommand?.RaiseCanExecuteChanged(); StartSceneCommand?.RaiseCanExecuteChanged(); PauseSceneCommand?.RaiseCanExecuteChanged(); ToggleSceneStartPauseCommand?.RaiseCanExecuteChanged(); NextSceneTurnCommand?.RaiseCanExecuteChanged(); ChooseSceneSpeakerCommand?.RaiseCanExecuteChanged(); SendGroupMessageCommand?.RaiseCanExecuteChanged(); FinishSceneCommand?.RaiseCanExecuteChanged();
    }
    private void RefreshSceneMessageSearchResults() =>
        RefreshMessageSearchResults(SceneMessageSearchResults, null, SceneMessages, SceneMessageSearchQuery, null, SelectedGroupConversation?.Conversation);

    private void SelectSceneMessageSearchResult(ChatMessageSearchResult? result) =>
        SelectSearchResult(result, null, SceneMessages, msg => ((SceneMessageViewModel)msg).Id == result!.MessageId);
}
