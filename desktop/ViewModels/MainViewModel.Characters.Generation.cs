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
    private async Task CreateChatForCharacterIdAsync(Guid characterId, string characterName, string chatName)
    {
        try
        {
            IsBusy = true;
            var created = await _conversations.CreateAsync([characterId], chatName);
            var conversation = created.Conversation;
            await ReloadCharactersAsync(characterId);
            SelectedPersonalConversation = new PersonalConversationEditorViewModel(conversation);
            RebuildChatCharacters();
            Status = $"Создан чат «{conversation.Name}» для персонажа «{characterName}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать чат", ex); }
        finally { IsBusy = false; }
    }
    private void ToggleCharacterCardSection(string? section)
    {
        if (!CharacterCardSections.TryToggle(section, ref _isCharacterDescriptionExpanded, ref _isCharacterPersonalityExpanded, ref _isCharacterScenarioExpanded))
            return;
        RaiseChatPresentationProperties();
    }
    private async Task ExpandCharacterFieldAsync(string? field)
    {
        if (SelectedCharacter is null) return;
        var resolved = CharacterCardGenerationService.ResolveExpandField(SelectedCharacter, field);
        if (resolved is null) return;
        var (normalized, fieldName, source) = resolved.Value;
        if (string.IsNullOrWhiteSpace(source))
        {
            Status = $"Сначала добавьте хотя бы короткую основу в поле «{fieldName}».";
            return;
        }
        try
        {
            IsBusy = true;
            Status = $"Локальная модель дополняет поле «{fieldName}»…";
            var settings = await BuildLlamaSettingsAsync();
            var request = CharacterCardGenerationService.BuildExpandFieldMessages(fieldName, source);
            var rawResponse = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, request, CancellationToken.None, $"character_field_{normalized}").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            });
            var addition = CharacterCardGenerationService.NormalizeFieldAddition(rawResponse, source);
            if (string.IsNullOrWhiteSpace(addition))
            {
                Status = "Локальная модель не вернула подходящее дополнение. Попробуйте ещё раз.";
                return;
            }
            var updated = CharacterCardGenerationService.MergeFieldAddition(source, addition);
            CharacterCardGenerationService.ApplyExpandedField(SelectedCharacter, normalized, updated);
            OnPropertyChanged(nameof(SelectedCharacter));
            Status = $"Поле «{fieldName}» дополнено локальной моделью. Проверьте текст и сохраните карточку персонажа.";
        }
        catch (Exception ex) { HandleError("Не удалось дополнить поле персонажа локальной моделью", ex); }
        finally { IsBusy = false; }
    }
    private async Task GenerateCharacterFromIdeaAsync()
    {
        var idea = CharacterGenerationIdea.Trim();
        if (string.IsNullOrWhiteSpace(idea)) return;
        try
        {
            IsBusy = true;
            Status = "Локальная модель создаёт карточку персонажа…";
            var settings = await BuildLlamaSettingsAsync();
            var request = CharacterCardGenerationService.BuildGenerateFromIdeaMessages(idea);
            var raw = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in GenerateWithPromptPolicyAsync(settings, request, CancellationToken.None, "character_card_generator").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            });
            var generated = CharacterCardGenerationService.ParseGeneratedCharacter(raw);
            if (generated is null || string.IsNullOrWhiteSpace(generated.Name))
            {
                Status = "Модель вернула карточку в непонятном формате. Попробуйте уточнить идею и повторить генерацию.";
                return;
            }
            var character = await _library.CreateCharacterAsync(generated.Name);
            CharacterCardGenerationService.ApplyGeneratedCard(character, generated);
            await _library.UpdateCharacterAsync(character);
            CharacterGenerationIdea = "";
            IsCharacterGeneratorOpen = false;
            await ReloadCharactersAsync(character.Id);
            CharacterEditorTab = "info";
            CloseCharacterCreationDialog();
            CurrentPage = "Characters";
            Status = $"Персонаж «{character.Name}» создан локальной моделью. Проверьте поля и сохраните карточку при необходимости.";
        }
        catch (Exception ex) { HandleError("Не удалось сгенерировать карточку персонажа локальной моделью", ex); }
        finally { IsBusy = false; }
    }
}
