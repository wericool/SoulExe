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
    private Task UseInstalledModelAsync() => SelectInstalledModelAsync(SelectedInstalledModel);
    private void LoadLlamaOptions(AppPreferences p) => LlamaOptions.ApplyFromPreferences(p);
    private async Task DownloadRecommendedModelAsync()
    {
        await BeginRecommendedModelDownloadAsync(initialSetup: false);
    }
    private async Task<bool> BeginRecommendedModelDownloadAsync(bool initialSetup)
    {
        if (SelectedRecommendedModel is null) return false;
        try
        {
            var recommendation = SelectedRecommendedModel;
            SetDownloadStatus($"Получаю кванты: {recommendation.RepositoryId}…", initialSetup);
            var files = await _modelsHub.GetGgufFilesAsync(recommendation.RepositoryId);
            var file = files.FirstOrDefault(x => x.Path.Contains(recommendation.OptimalQuant, StringComparison.OrdinalIgnoreCase))
                       ?? files.FirstOrDefault();
            if (file is null) throw new InvalidOperationException("В репозитории рекомендации не найден GGUF-файл.");
            var request = new ModelDownloadRequest(recommendation.RepositoryId, file, recommendation.Name, initialSetup, true);
            return await StartModelDownloadAsync(request);
        }
        catch (Exception ex)
        {
            HandleError("Не удалось подготовить загрузку рекомендуемой модели", ex);
            return false;
        }
    }
    private async Task DownloadSelectedModelAsync()
    {
        if (SelectedModelResult is null || SelectedModelFile is null) return;
        var request = new ModelDownloadRequest(SelectedModelResult.RepositoryId, SelectedModelFile, null, false, false);
        await StartModelDownloadAsync(request);
    }
    private async Task ResumeModelDownloadAsync()
    {
        if (_resumableDownload is null) return;
        await StartModelDownloadAsync(_resumableDownload);
    }
    private void ToggleModelDownload()
    {
        if (IsModelDownloadInProgress)
        {
            PauseModelDownload();
            return;
        }
        _ = ResumeModelDownloadAsync();
    }
    private async Task<bool> StartModelDownloadAsync(ModelDownloadRequest request)
    {
        CancellationTokenSource? cts = null;
        try
        {
            IsBusy = true;
            IsModelDownloadInProgress = true;
            CanResumeModelDownload = false;
            _downloadPauseRequested = false;
            _downloadCancelRequested = false;
            _resumableDownload = request;
            cts = new CancellationTokenSource();
            _modelDownloadCts = cts;
            SetDownloadStatus($"Подготавливаю скачивание {request.File.Path}…", request.IsInitialSetup);

            var model = await _modelsHub.DownloadModelAsync(
                request.RepositoryId,
                request.File,
                progress => UpdateDownloadProgress(request, progress),
                status => SetDownloadStatus(status, request.IsInitialSetup),
                cts.Token);

            ModelPath = model.LocalPath;
            ModelRepository = request.RepositoryId;
            await RefreshInstalledModelsAsync();
            _resumableDownload = null;
            CanResumeModelDownload = false;
            if (request.IsInitialSetup)
            {
                SetupModelDownloaded = true;
                SetupModelDownloadPercent = 100;
                SetupProgressText = "Модель скачана. Нажмите «Запустить и открыть чат».";
            }
            if (request.IsRecommended)
            {
                ModelDownloadStatus = $"Рекомендуемая модель готова: {model.DisplayName}";
                Status = $"Выбрана рекомендация «{request.RecommendationName}».";
                RecommendedModelSizeInfo = $"Загружен {model.DisplayName} • размер: {model.SizeBytes / 1_073_741_824d:F1} ГБ";
            }
            else
            {
                ModelDownloadStatus = $"Модель готова: {model.DisplayName}";
                Status = "GGUF-модель сохранена локально и выбрана для запуска.";
            }
            return true;
        }
        catch (OperationCanceledException) when (_downloadCancelRequested)
        {
            _modelsHub.DiscardPartialDownload(request.RepositoryId, request.File);
            _resumableDownload = null;
            CanResumeModelDownload = false;
            SetDownloadStatus("Загрузка отменена. Частичный файл удалён из SoulExeData.", request.IsInitialSetup);
            Status = "Загрузка модели отменена.";
            return false;
        }
        catch (OperationCanceledException) when (_downloadPauseRequested)
        {
            CanResumeModelDownload = _resumableDownload is not null;
            SetDownloadStatus("Загрузка поставлена на паузу. Полученная часть модели сохранена в SoulExeData; нажмите «Продолжить» после восстановления интернета.", request.IsInitialSetup);
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Write("Загрузка модели прервана; частичный файл сохранён для продолжения.", ex);
            CanResumeModelDownload = _resumableDownload is not null;
            SetDownloadStatus($"Загрузка прервана: {ex.Message} Полученная часть сохранена. Нажмите «Продолжить», чтобы докачать файл.", request.IsInitialSetup);
            Status = "Загрузка модели прервана. Частичный файл сохранён локально.";
            return false;
        }
        finally
        {
            if (ReferenceEquals(_modelDownloadCts, cts)) _modelDownloadCts = null;
            cts?.Dispose();
            IsModelDownloadInProgress = false;
            IsBusy = false;
        }
    }
    private void UpdateDownloadProgress(ModelDownloadRequest request, ModelDownloadProgress progress)
    {
        var text = ModelDownloadStatusText.Progress(request.File.Path, progress.Display);
        if (request.IsInitialSetup)
        {
            SetupModelDownloadPercent = progress.Percent;
            SetupProgressText = text;
        }
        else
        {
            ModelDownloadStatus = text;
        }
    }
    private void SetDownloadStatus(string text, bool initialSetup)
    {
        if (initialSetup) SetupProgressText = text;
        else ModelDownloadStatus = text;
    }
    private void SetActiveDownloadStatus(string text) =>
        SetDownloadStatus(text, _resumableDownload?.IsInitialSetup == true);
    private void PauseModelDownload()
    {
        if (_modelDownloadCts is null || _modelDownloadCts.IsCancellationRequested) return;
        _downloadPauseRequested = true;
        SetActiveDownloadStatus(ModelDownloadStatusText.Pausing);
        _modelDownloadCts.Cancel();
    }
    private void CancelModelDownload()
    {
        var request = _resumableDownload;
        if (request is null) return;
        _downloadCancelRequested = true;
        if (_modelDownloadCts is not null && !_modelDownloadCts.IsCancellationRequested)
        {
            SetActiveDownloadStatus(ModelDownloadStatusText.Cancelling);
            _modelDownloadCts.Cancel();
            return;
        }

        var initialSetup = request.IsInitialSetup;
        _modelsHub.DiscardPartialDownload(request.RepositoryId, request.File);
        _resumableDownload = null;
        CanResumeModelDownload = false;
        SetDownloadStatus(ModelDownloadStatusText.Cancelled, initialSetup);
        Status = "Загрузка модели отменена.";
    }
}
