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
    private async Task SearchModelsAsync()
    {
        try
        {
            IsBusy = true;
            ModelsCatalogError = null;
            ModelDownloadStatus = "Ищу GGUF-модели на Hugging Face…";
            var results = await _modelsHub.SearchAsync(ModelSearchQuery);
            ModelSearchResults.Clear();
            foreach (var result in results) ModelSearchResults.Add(result);
            ModelFiles.Clear();
            SelectedModelResult = ModelSearchResults.FirstOrDefault();
            ModelDownloadStatus = results.Count == 0 ? "Поиск не дал результатов." : $"Найдено репозиториев: {results.Count}. Выберите модель.";
        }
        catch (Exception ex)
        {
            ModelsCatalogError = "Не удалось выполнить поиск моделей. Проверьте подключение и повторите попытку.";
            ModelDownloadStatus = ModelsCatalogError;
            HandleError("Не удалось выполнить поиск моделей", ex);
        }
        finally { IsBusy = false; }
    }
    private async Task LoadModelDetailsAsync(ModelHubSearchResult? result)
    {
        SelectedModelDetails = null;
        if (result is null) return;
        try { SelectedModelDetails = await _modelsHub.GetDetailsAsync(result); }
        catch (Exception ex) { AppLog.Write("Не удалось получить описание репозитория GGUF.", ex); }
    }
    private async Task LoadModelFilesAsync(ModelHubSearchResult? result)
    {
        ModelFiles.Clear();
        SelectedModelFile = null;
        if (result is null) return;
        try
        {
            ModelDownloadStatus = $"Получаю GGUF-файлы: {result.RepositoryId}…";
            var files = await _modelsHub.GetGgufFilesAsync(result.RepositoryId);
            foreach (var file in files) ModelFiles.Add(file);
            SelectedModelFile = ModelFiles.FirstOrDefault();
            ModelDownloadStatus = files.Count == 0 ? "В этом репозитории не найдено GGUF-файлов." : $"Выберите квант и нажмите «Скачать»";
        }
        catch (Exception ex) { HandleError("Не удалось получить список GGUF-файлов", ex); }
    }
    private async Task RefreshInstalledModelsAsync()
    {
        try
        {
            var models = await _modelsHub.RefreshInstalledModelsAsync();
            InstalledModels.Clear();
            foreach (var model in models) InstalledModels.Add(model);
            SelectedInstalledModel = InstalledModels.FirstOrDefault(x => string.Equals(x.LocalPath, ModelPath, StringComparison.OrdinalIgnoreCase));
            RefreshRecommendedInstallationState();
            OnPropertyChanged(nameof(HasInstalledModels));
        }
        catch (Exception ex) { AppLog.Write("Installed model library refresh failed.", ex); }
    }
    private async Task SelectInstalledModelAsync(SoulModelInstallation? model)
    {
        if (model is null || string.Equals(ModelPath, model.LocalPath, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            IsBusy = true;
            if (_llama.IsStartedByApplication)
            {
                await _llama.StopAsync();
                Status = "Предыдущая модель остановлена. Выбрана новая модель; нажмите «Запустить».";
            }
            ModelRepository = "";
            ModelPath = model.LocalPath;
            OnPropertyChanged(nameof(ModelSourceText));
            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(IsModelRunning));
            OnPropertyChanged(nameof(ModelStartStopText));
            Status = $"Выбрана локальная модель: {model.DisplayName}. Нажмите «Запустить».";
        }
        catch (Exception ex) { HandleError("Не удалось переключить локальную модель", ex); }
        finally
        {
            IsBusy = false;
            RaiseAllCommands();
        }
    }
    private async Task DownloadInitialRecommendedModelAsync()
    {
        SetupModelDownloaded = false;
        SetupModelDownloadPercent = 0;
        var completed = await BeginRecommendedModelDownloadAsync(initialSetup: true);
        if (!completed) return;
        SetupModelDownloaded = true;
        SetupModelDownloadPercent = 100;
        SetupProgressText = "Модель скачана. Нажмите «Запустить и открыть чат».";
    }
    private async Task LoadRecommendedModelSizeAsync(RecommendedModel? recommendation)
    {
        if (recommendation is null) return;
        try
        {
            RecommendedModelSizeInfo = $"Получаю размер {recommendation.OptimalQuant}…";
            var files = await _modelsHub.GetGgufFilesAsync(recommendation.RepositoryId);
            var file = files.FirstOrDefault(x => x.Path.Contains(recommendation.OptimalQuant, StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
            RecommendedModelSizeInfo = file is null
                ? "Размер GGUF-файла не найден в репозитории."
                : $"Рекомендуемый файл: {file.Path} • размер: {file.DisplaySize}";
        }
        catch
        {
            RecommendedModelSizeInfo = $"Квант {recommendation.OptimalQuant}; размер будет проверен перед загрузкой.";
        }
    }
    private async Task LoadRecommendedModelsAsync(bool forceRefresh)
    {
        // Always show the complete embedded catalog first. This keeps Recommendations usable
        // while the network or an old partial cache is unavailable.
        var embedded = _recommendedModels.GetEmbeddedCatalog();
        ReplaceRecommendedModels(embedded);
        ModelDownloadStatus = $"Рекомендации доступны: {RecommendedModels.Count} моделей.";
        try
        {
            var models = await _recommendedModels.GetAsync(forceRefresh);
            if (models.Count >= 10)
            {
                ReplaceRecommendedModels(models);
                ModelDownloadStatus = $"Рекомендации обновлены: {RecommendedModels.Count} моделей.";
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Не удалось обновить каталог рекомендаций; используется встроенная версия.", ex);
            ModelDownloadStatus = $"Используется встроенный каталог: {RecommendedModels.Count} моделей.";
        }
    }
    private void ReplaceRecommendedModels(IReadOnlyList<RecommendedModel> models)
    {
        var selectedId = SelectedRecommendedModel?.RepositoryId;
        RecommendedModels.Clear();
        foreach (var model in models) RecommendedModels.Add(WithInstallationState(model));
        SelectedRecommendedModel = RecommendedModels.FirstOrDefault(x => x.RepositoryId == selectedId) ?? RecommendedModels.FirstOrDefault();
        OnPropertyChanged(nameof(RecommendedModelDownloadText));
        DownloadRecommendedModelCommand.RaiseCanExecuteChanged();
        SetupDownloadRecommendedCommand.RaiseCanExecuteChanged();
    }
    private void RefreshRecommendedInstallationState()
    {
        if (RecommendedModels.Count == 0) return;
        ReplaceRecommendedModels(RecommendedModels.ToList());
    }
    private RecommendedModel WithInstallationState(RecommendedModel model) =>
        RecommendedCatalog.WithInstallationState(model, InstalledModels);
}
