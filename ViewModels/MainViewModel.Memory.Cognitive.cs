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
    private async Task SaveSelectedCharacterCognitiveArchitectureAsync(Guid characterId, bool enabled)
    {
        try { await _library.SetCognitiveArchitectureEnabledAsync(characterId, enabled); }
        catch (Exception ex) { AppLog.Write("Не удалось сохранить настройку Cognitive Architecture персонажа.", ex); }
    }
    private async Task SaveCognitiveArchitectureAsync()
    {
        try
        {
            var memoryEnabled = _cognitiveSoulMemoryEnabled;
            var memoryPreset = _selectedSoulMemoryPreset;
            var memoryInterval = _cognitiveMemoryIntervalMessages;
            var summaryEnabled = _cognitiveAutoSummaryEnabled;
            var summaryInterval = _cognitiveSummaryIntervalMessages;
            var backgroundMode = _cognitiveBackgroundMode;
            var backgroundIdleSeconds = _cognitiveBackgroundIdleSeconds;
            await _store.MutateAsync(root => CognitivePreferences.Write(
                root.Preferences,
                memoryEnabled,
                memoryPreset,
                memoryInterval,
                summaryEnabled,
                summaryInterval,
                backgroundMode,
                backgroundIdleSeconds), "save_cognitive_architecture");
        }
        catch (Exception ex) { AppLog.Write("Не удалось сохранить настройки Cognitive Architecture.", ex); }
    }
    private async Task UpdateCurrentMemoryAsync()
    {
        if (SelectedCharacter is null || SelectedPersonalConversation is null) return;
        if (!SelectedCharacterCognitiveArchitectureEnabled || !SelectedCharacter.SoulMemoryEnabled)
        {
            Status = "Soul Memory отключена для выбранного персонажа. Включите её в карточке и сохраните настройки памяти.";
            return;
        }
        try
        {
            _cognitiveScheduler.Cancel(SelectedCharacter.Id, SelectedPersonalConversation.Id);
            IsBusy = true;
            var result = await AppServices.SoulMemory.UpdateAfterConversationAsync(SelectedCharacter.Id, SelectedPersonalConversation.Id, CompleteForMemoryAsync, force: true, intervalMessages: SelectedCharacter.SoulMemoryIntervalMessages, preset: SelectedCharacter.SoulMemoryPreset);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = result.Status;
        }
        catch (Exception ex) { HandleError("Не удалось обновить Soul Memory", ex); }
        finally { IsBusy = false; }
    }
    private async Task UpdateCurrentSummaryAsync()
    {
        if (SelectedCharacter is null || SelectedPersonalConversation is null) return;
        if (!SelectedCharacterCognitiveArchitectureEnabled || !SelectedCharacter.AutoSummaryEnabled)
        {
            Status = "Auto-Summary отключено для выбранного персонажа. Включите его в карточке и сохраните настройки памяти.";
            return;
        }
        try
        {
            _cognitiveScheduler.Cancel(SelectedCharacter.Id, SelectedPersonalConversation.Id);
            IsBusy = true;
            var result = await AppServices.Summaries.UpdateAsync(SelectedCharacter.Id, SelectedPersonalConversation.Id, CompleteForMemoryAsync, force: true, intervalMessages: SelectedCharacter.AutoSummaryIntervalMessages);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = result.Status;
        }
        catch (Exception ex) { HandleError("Не удалось обновить summary", ex); }
        finally { IsBusy = false; }
    }
    private async Task ScheduleCognitiveMaintenanceAsync(Guid characterId, Guid chatId)
    {
        var character = await _store.ReadAsync(root => root.Characters.FirstOrDefault(item => item.Id == characterId));
        if (character is null || !character.CognitiveArchitectureEnabled || (!character.SoulMemoryEnabled && !character.AutoSummaryEnabled)) return;
        var memoryEnabled = character.SoulMemoryEnabled;
        var memoryInterval = character.SoulMemoryIntervalMessages;
        var memoryPreset = character.SoulMemoryPreset;
        var summaryEnabled = character.AutoSummaryEnabled;
        var summaryInterval = character.AutoSummaryIntervalMessages;
        _cognitiveScheduler.Schedule(characterId, chatId, CognitiveBackgroundMode, CognitiveBackgroundIdleSeconds, async token =>
        {
            var statuses = new List<string>();
            // A summary is one concise call, while Full Soul Memory can be three or more calls.
            // When both are due, preserve the responsive path: update the summary now and defer
            // detailed memory until the next genuine idle window.
            if (summaryEnabled)
            {
                var summary = await AppServices.Summaries.UpdateAsync(characterId, chatId, CompleteForMemoryAsync, token, intervalMessages: summaryInterval);
                if (summary.Updated)
                {
                    AppLog.Write($"COGNITIVE_MAINTENANCE_DECISION character={characterId:N} chat={chatId:N} action=summary_first_memory_deferred");
                    statuses.Add(summary.Status + " Детальная Soul Memory отложена до следующей паузы.");
                }
                else if (!summary.Skipped)
                {
                    statuses.Add(summary.Status);
                }
            }

            if (statuses.Count == 0 && memoryEnabled)
            {
                var memory = await AppServices.SoulMemory.UpdateAfterConversationAsync(characterId, chatId, CompleteForMemoryAsync, token, intervalMessages: memoryInterval, preset: memoryPreset);
                if (memory.Updated) statuses.Add(memory.Status);
            }
            if (statuses.Count > 0)
            {
                ReportCognitiveBackground(string.Join(" ", statuses));
                await RefreshCognitiveUiAsync(characterId).ConfigureAwait(false);
            }
            else
            {
                ReportCognitiveBackground("Новых данных для фонового обновления памяти нет.");
            }
        });
    }
    private async Task ScheduleCognitiveMaintenanceAfterReplyAsync(Guid characterId, Guid chatId)
    {
        try
        {
            await ScheduleCognitiveMaintenanceAsync(characterId, chatId);
        }
        catch (Exception ex)
        {
            AppLog.Write("Не удалось запланировать фоновое обновление памяти", ex);
        }
    }
    private Task RefreshCognitiveUiAsync(Guid characterId) =>
        UiThread.InvokeAsync(async () =>
        {
            if (SelectedCharacter?.Id == characterId)
                await ReloadCharactersAsync(characterId);
        });
    private void ReportCognitiveBackground(string message) =>
        UiThread.BeginInvoke(() => CognitiveBackgroundStatus = message);
    private async Task<string> CompleteForMemoryAsync(IReadOnlyList<LlamaMessage> messages, CancellationToken token)
    {
        var settings = LlamaSettingsFactory.ForCognitiveMaintenance(await BuildLlamaSettingsAsync());
        AppLog.Write($"COGNITIVE_MAINTENANCE_REQUEST messages={messages.Count} maxTokens={settings.MaxTokens} temperature={settings.Temperature:0.###}");
        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, messages, token)) answer.Append(chunk);
        return answer.ToString();
    }
}
