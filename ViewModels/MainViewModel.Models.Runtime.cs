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
    private async Task SaveModelSettingsAsync()
    {
        try
        {
            NormalizeDiscreteGenerationLimits();
            await _store.MutateAsync(root => LlamaOptions.WriteToPreferences(root.Preferences), "save_model_settings");
            Status = "Расширенные настройки llama.cpp сохранены рядом с программой.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить настройки модели", ex); }
    }
    private async Task SelectAndInstallInitialBackendAsync(string? backendId)
    {
        var backend = _installer.GetBackend(backendId);
        SelectedLlamaBackend = LlamaBackends.FirstOrDefault(x => x.Id == backend.Id) ?? backend;
        await InstallInitialEngineAsync();
    }
    private async Task InstallInitialEngineAsync()
    {
        try
        {
            IsBusy = true;
            SetupProgressPercent = 0;
            var backend = _installer.GetBackend(LlamaOptions.EngineBackend);
            SetupProgressText = $"Подготовка {backend.DisplayName}…";
            ServerPath = await _installer.InstallEngineAsync(backend.Id, message =>
            {
                SetupProgressText = message;
                if (ProgressTextParser.TryReadPercent(message, out var percent))
                    SetupProgressPercent = percent;
            });
            await SaveModelSettingsAsync();
            SetupProgressPercent = 100;
            SetupProgressText = $"{backend.DisplayName} установлен. Теперь доступна кнопка «Далее: выбрать модель».";
            Status = SetupProgressText;
            RefreshBackendInstallStates();
            OnPropertyChanged(nameof(CanInstallSelectedBackend));
        }
        catch (Exception ex) { HandleError("Не удалось установить выбранный backend llama.cpp", ex); }
        finally { IsBusy = false; }
    }
    private void RefreshBackendInstallStates()
    {
        foreach (var backend in LlamaBackends)
            backend.IsInstalled = _installer.IsBackendInstalled(backend.Id);
        var selectedId = SelectedLlamaBackend?.Id;
        var snapshot = LlamaBackends.ToList();
        LlamaBackends.Clear();
        foreach (var backend in snapshot)
            LlamaBackends.Add(backend);
        SelectedLlamaBackend = LlamaBackends.FirstOrDefault(x => x.Id == selectedId) ?? LlamaBackends.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedBackendInstallState));
        SetupInstallEngineCommand.RaiseCanExecuteChanged();
        NextInitialSetupStepCommand.RaiseCanExecuteChanged();
        PreviousInitialSetupStepCommand.RaiseCanExecuteChanged();
    }
    private async Task MoveToModelStepAsync()
    {
        if (!_installer.IsBackendInstalled(LlamaOptions.EngineBackend))
        {
            Status = "Сначала выберите и установите движок llama.cpp.";
            return;
        }
        InitialSetupStep = 2;
        PreviousInitialSetupStepCommand.RaiseCanExecuteChanged();
        if (RecommendedModels.Count == 0) await LoadRecommendedModelsAsync(false);
    }
    private async Task InstallEngineAsync()
    {
        try
        {
            IsBusy = true;
            var backend = _installer.GetBackend(LlamaOptions.EngineBackend);
            Status = $"Подготавливаю {backend.DisplayName}…";
            ServerPath = await _installer.InstallEngineAsync(backend.Id, message => Status = message);
            await SaveModelSettingsAsync();
            OnPropertyChanged(nameof(SelectedBackendInstallState));
            OnPropertyChanged(nameof(CanInstallSelectedBackend));
            SetupInstallEngineCommand.RaiseCanExecuteChanged();
            Status = $"{backend.DisplayName}: llama.cpp установлен. Выберите модель и запустите сервер.";
        }
        catch (Exception ex) { HandleError("Не удалось установить выбранный backend llama.cpp", ex); }
        finally { IsBusy = false; }
    }
    private Task UseStarterModelAsync()
    {
        ModelRepository = StarterModelDefaults.HuggingFaceRepository;
        ModelPath = "";
        OnPropertyChanged(nameof(ModelSourceText));
        Status = StarterModelDefaults.SelectedMessage;
        return Task.CompletedTask;
    }
    private async Task ToggleModelStartStopAsync()
    {
        if (_llama.IsStartedByApplication) await StopModelAsync();
        else await StartModelAsync();
    }
    private async Task StartModelAsync()
    {
        try
        {
            IsBusy = true;
            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(ModelStartStopText));
            Status = "Загружаю GGUF-модель и ожидаю готовность llama.cpp…";
            var settings = await BuildLlamaSettingsAsync();
            await _llama.StartAsync(settings);
            _isModelApiAvailable = await _llama.IsAvailableAsync(settings);
            if (!_isModelApiAvailable)
                throw new InvalidOperationException("llama.cpp завершила запуск без доступного API. Откройте диагностику запуска модели.");
            Status = "Локальная модель готова к диалогу.";
        }
        catch (Exception ex) { HandleError("Не удалось запустить модель", ex); }
        finally
        {
            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(IsModelRunning));
            OnPropertyChanged(nameof(ModelStartStopText));
            OnPropertyChanged(nameof(ModelLaunchDiagnostic));
            OnPropertyChanged(nameof(ModelLaunchCommand));
            OnPropertyChanged(nameof(SelectedCharacterPresence));
            StopModelCommand.RaiseCanExecuteChanged();
            ToggleModelStartStopCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }
    private async Task StopModelAsync()
    {
        try
        {
            IsBusy = true;
            await _llama.StopAsync();
            _isModelApiAvailable = false;
            Status = "Локальная модель остановлена.";
        }
        catch (Exception ex) { HandleError("Не удалось остановить модель", ex); }
        finally
        {
            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(IsModelRunning));
            OnPropertyChanged(nameof(ModelStartStopText));
            OnPropertyChanged(nameof(ModelLaunchDiagnostic));
            OnPropertyChanged(nameof(SelectedCharacterPresence));
            StopModelCommand.RaiseCanExecuteChanged();
            ToggleModelStartStopCommand.RaiseCanExecuteChanged();
            IsBusy = false;
        }
    }
    private async Task ChooseModelAsync()
    {
        var dialog = new OpenFileDialog { Filter = "GGUF model|*.gguf|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var source = dialog.FileName;
            ModelRepository = "";
            ModelPath = source;
            await _modelsHub.RegisterExistingModelAsync(source);
            await RefreshInstalledModelsAsync();
            OnPropertyChanged(nameof(ModelSourceText));
            Status = $"Выбран существующий GGUF: {Path.GetFileName(source)}";
        }
        catch (Exception ex) { HandleError("Не удалось добавить GGUF в библиотеку", ex); }
        finally { IsBusy = false; }
    }
    private async Task<AppSettings> BuildLlamaSettingsAsync()
    {
        var data = await _store.ReadAsync(root => root.Preferences);
        return LlamaSettingsFactory.Build(LlamaOptions, ServerPath, ModelPath, ModelRepository, data.NetworkPort);
    }
}
