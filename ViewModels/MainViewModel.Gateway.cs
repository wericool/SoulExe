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
    private async Task LoadGatewayAsync(bool append = false)
    {
        try
        {
            IsBusy = true;
            GatewayError = null;
            if (!append)
            {
                _gatewayPage = 1;
                GatewayItems.Clear();
                GatewayHasMore = GatewayCategory == "chub";
            }
            else _gatewayPage++;

            var results = await _charactersGateway.GetAssetsAsync(GatewayCategory, GatewayQuery, GatewayNsfwEnabled, _gatewayPage);
            GatewayCatalog.MergePage(GatewayItems, results, Lorebooks.Select(x => x.Name), out var hasMorePage);
            GatewayHasMore = GatewayCategory == "chub" && hasMorePage;
            SelectedGatewayAsset = GatewayItems.FirstOrDefault();
            Status = GatewayCatalog.StatusLine(GatewayCategoryTitle, GatewayItems.Count, GatewayHasMore);
        }
        catch (Exception ex)
        {
            if (append) _gatewayPage = Math.Max(1, _gatewayPage - 1);
            GatewayError = $"Не удалось загрузить «{GatewayCategoryTitle}». Проверьте подключение и повторите попытку.";
            HandleError($"Не удалось получить «{GatewayCategoryTitle}" + "»", ex);
        }
        finally { IsBusy = false; }
    }
    private async Task ImportGatewayAssetAsync()
    {
        if (SelectedGatewayAsset is null) return;
        try
        {
            IsBusy = true;
            switch (SelectedGatewayAsset.Kind)
            {
                case "soul":
                {
                    Status = "Скачивается Character Card V2 из Soul Gateway…";
                    var path = await _charactersGateway.DownloadOfficialCharacterCardAsync(SelectedGatewayAsset);
                    var character = await _characterCards.ImportAsync(path);
                    await ReloadCharactersAsync(character.Id);
                    Status = $"Импортирован персонаж «{character.Name}» из Soul Gateway.";
                    break;
                }
                case "chub":
                {
                    Status = "Загружается карточка Chub AI…";
                    var details = await _charactersGateway.GetDetailsAsync(SelectedGatewayAsset.Id);
                    var character = await _charactersGateway.ImportChubCharacterAsync(details);
                    await ReloadCharactersAsync(character.Id);
                    Status = $"Импортирован персонаж «{character.Name}» из Chub AI.";
                    break;
                }
                case "lorebook":
                {
                    Status = "Импортируется World Lorebook…";
                    var lorebook = await _charactersGateway.ImportLorebookAsync(SelectedGatewayAsset);
                    await ReloadLorebooksAsync();
                    SelectedLorebook = Lorebooks.FirstOrDefault(x => x.Id == lorebook.Id);
                    SelectedGatewayAsset.IsAlreadyImported = true;
                    foreach (var item in GatewayItems.Where(x => x.Kind == "lorebook" && string.Equals(x.Name, lorebook.Name, StringComparison.CurrentCultureIgnoreCase)))
                        item.IsAlreadyImported = true;
                    OnPropertyChanged(nameof(SelectedGatewayAsset));
                    ImportGatewayAssetCommand.RaiseCanExecuteChanged();
                    Status = $"Импортирован лорбук «{lorebook.Name}». При необходимости привяжите его к персонажу на странице «Память и лор».";
                    break;
                }
                case "scenario":
                {
                    Status = "Создаётся текстовый чат по сценарию…";
                    var character = await _charactersGateway.ImportTextScenarioAsync(SelectedGatewayAsset);
                    await ReloadCharactersAsync(character.Id);
                    CurrentPage = "Chat";
                    Status = $"Создан текстовый сценарий «{SelectedGatewayAsset.Name}». Открыт чат без Soul Stage.";
                    break;
                }
            }
        }
        catch (Exception ex) { HandleError("Не удалось импортировать материал из Characters Gateway", ex); }
        finally { IsBusy = false; }
    }
    private async Task SaveGatewayPreferencesAsync()
    {
        try
        {
            var category = _gatewayCategory;
            var nsfw = _gatewayNsfwEnabled;
            await _store.MutateAsync(root =>
            {
                root.Preferences.GatewayCategory = category;
                root.Preferences.GatewayNsfwEnabled = nsfw;
            }, "save_gateway_preferences");
        }
        catch (Exception ex) { AppLog.Write("Не удалось сохранить настройки Characters Gateway.", ex); }
    }
}
