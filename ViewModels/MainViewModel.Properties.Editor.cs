using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    public string CharacterEditorTab
    {
        get => _characterEditorTab;
        set
        {
            var tab = CharacterEditorTabs.Normalize(value);
            if (!Set(ref _characterEditorTab, tab)) return;
            OnPropertyChanged(nameof(IsCharacterEditorInfoTab));
            OnPropertyChanged(nameof(IsCharacterEditorMemoryTab));
            OnPropertyChanged(nameof(IsCharacterEditorLoreTab));
        }
    }
    public bool IsCharacterEditorInfoTab => CharacterEditorTab == "info";
    public bool IsCharacterEditorMemoryTab => CharacterEditorTab == "memory";
    public bool IsCharacterEditorLoreTab => CharacterEditorTab == "lore";

    public bool IsAssistantTyping { get => _isAssistantTyping; private set => Set(ref _isAssistantTyping, value); }
    public bool IsCharacterGeneratorOpen { get => _isCharacterGeneratorOpen; set => Set(ref _isCharacterGeneratorOpen, value); }
    public bool IsCharacterCreationDialogOpen { get => _isCharacterCreationDialogOpen; private set => Set(ref _isCharacterCreationDialogOpen, value); }
    public string CharacterCreationMode
    {
        get => _characterCreationMode;
        private set
        {
            if (!Set(ref _characterCreationMode, value)) return;
            OnPropertyChanged(nameof(IsManualCharacterCreationMode));
            OnPropertyChanged(nameof(IsGeneratedCharacterCreationMode));
        }
    }
    public bool IsManualCharacterCreationMode => CharacterCreationMode == "manual";
    public bool IsGeneratedCharacterCreationMode => CharacterCreationMode == "generate";
    public string CharacterNameDraft
    {
        get => _characterNameDraft;
        set
        {
            if (!Set(ref _characterNameDraft, value)) return;
            CreateCharacterWithNameCommand.RaiseCanExecuteChanged();
        }
    }
    public SoulCharacter? CharacterPendingDeletion
    {
        get => _characterPendingDeletion;
        private set
        {
            if (!Set(ref _characterPendingDeletion, value)) return;
            OnPropertyChanged(nameof(IsCharacterDeleteDialogOpen));
            ConfirmCharacterDeleteCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsCharacterDeleteDialogOpen => CharacterPendingDeletion is not null;
    public string CharacterGenerationIdea
    {
        get => _characterGenerationIdea;
        set
        {
            if (!Set(ref _characterGenerationIdea, value)) return;
            GenerateCharacterFromIdeaCommand.RaiseCanExecuteChanged();
        }
    }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsInitialSetupVisible { get => _isInitialSetupVisible; private set => Set(ref _isInitialSetupVisible, value); }
    public int InitialSetupStep
    {
        get => _initialSetupStep;
        private set
        {
            if (!Set(ref _initialSetupStep, value)) return;
            OnPropertyChanged(nameof(IsInitialSetupEngineStep));
            OnPropertyChanged(nameof(IsInitialSetupModelStep));
            OnPropertyChanged(nameof(InitialSetupStepTitle));
        }
    }
    public bool IsInitialSetupEngineStep => InitialSetupStep == 1;
    public bool IsInitialSetupModelStep => InitialSetupStep == 2;
    public string InitialSetupStepTitle => IsInitialSetupEngineStep ? "Шаг 1 из 2 — движок llama.cpp" : "Шаг 2 из 2 — первая модель";
    public double SetupProgressPercent { get => _setupProgressPercent; private set => Set(ref _setupProgressPercent, Math.Clamp(value, 0, 100)); }
    public double SetupModelDownloadPercent { get => _setupModelDownloadPercent; private set => Set(ref _setupModelDownloadPercent, Math.Clamp(value, 0, 100)); }
    public string SetupProgressText { get => _setupProgressText; private set => Set(ref _setupProgressText, value); }
    public bool SetupModelDownloaded { get => _setupModelDownloaded; private set { if (Set(ref _setupModelDownloaded, value)) SetupStartChatCommand.RaiseCanExecuteChanged(); } }
    public string RecommendedModelSizeInfo { get => _recommendedModelSizeInfo; private set => Set(ref _recommendedModelSizeInfo, value); }
    private void NavigateTo(string page)
    {
        var optionsTab = AppNavigation.OptionsTabForRoute(page);
        page = AppNavigation.NormalizePage(page);
        if (optionsTab is not null) SelectOptionsTab(optionsTab);
        CurrentPage = page;
        if (page == "Gateway" && GatewayItems.Count == 0 && !IsBusy) _ = LoadGatewayAsync();
        if (optionsTab == "models" && !IsBusy)
        {
            _ = RefreshInstalledModelsAsync();
            if (RecommendedModels.Count == 0) _ = LoadRecommendedModelsAsync(false);
        }
    }

    public string ModelsHubTab
    {
        get => _modelsHubTab;
        private set
        {
            if (!Set(ref _modelsHubTab, value)) return;
            OnPropertyChanged(nameof(IsModelsCatalogTab));
            OnPropertyChanged(nameof(IsModelsRecommendationsTab));
            OnPropertyChanged(nameof(IsModelsInstalledTab));
        }
    }
    public bool IsModelsCatalogTab => ModelsHubTab == "Catalog";
    public bool IsModelsRecommendationsTab => ModelsHubTab == "Recommendations";
    public bool IsModelsInstalledTab => ModelsHubTab == "Installed";

    private void SetModelsHubTab(string tab)
    {
        ModelsHubTab = AppNavigation.NormalizeModelsHubTab(tab);
        if (ModelsHubTab == "Recommendations" && RecommendedModels.Count == 0 && !IsBusy) _ = LoadRecommendedModelsAsync(false);
        if (ModelsHubTab == "Installed" && !IsBusy) _ = RefreshInstalledModelsAsync();
    }

    public string CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!Set(ref _currentPage, value)) return;
            if (!string.Equals(value, "Options", StringComparison.OrdinalIgnoreCase) && IsMobileOptionsTab)
                SelectOptionsTab("llm");
            OnPropertyChanged(nameof(IsHomePage));
            OnPropertyChanged(nameof(IsChatPage));
            OnPropertyChanged(nameof(IsScenePage));
            OnPropertyChanged(nameof(IsCharactersPage));
            OnPropertyChanged(nameof(IsGatewayPage));
            OnPropertyChanged(nameof(IsModelsPage));
            OnPropertyChanged(nameof(IsMemoryPage));
            OnPropertyChanged(nameof(IsMobilePage));
            OnPropertyChanged(nameof(IsOptionsPage));
            OnPropertyChanged(nameof(IsSetupPage));
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageSubtitle));
        }
    }
    public bool IsHomePage => CurrentPage == "Home";
    public bool IsChatPage => CurrentPage == "Chat";
    public bool IsScenePage => CurrentPage == "Scene";
    public bool IsCharactersPage => CurrentPage == "Characters";
    public bool IsGatewayPage => CurrentPage == "Gateway";
    public bool IsModelsPage => CurrentPage == "Models";
    public bool IsMemoryPage => CurrentPage == "Memory";
    public bool IsMobilePage => CurrentPage == "Mobile";
    public bool IsOptionsPage => CurrentPage == "Options";
    public bool IsSetupPage => CurrentPage == "Setup";

    public string PageTitle => AppNavigation.Title(CurrentPage);

    public string PageSubtitle => AppNavigation.Subtitle(CurrentPage);
    public string ServerPath { get => _serverPath; set { if (Set(ref _serverPath, value)) _ = SavePreferencesAsync(); } }
    public string ModelPath { get => _modelPath; set { if (Set(ref _modelPath, value)) _ = SavePreferencesAsync(); } }
    public string ModelRepository { get => _modelRepository; set { if (Set(ref _modelRepository, value)) _ = SavePreferencesAsync(); } }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(ModelState));
            OnPropertyChanged(nameof(CanInstallSelectedBackend));
            SetupInstallEngineCommand.RaiseCanExecuteChanged();
            RaiseAllCommands();
        }
    }
    public bool SelectedCharacterCognitiveArchitectureEnabled
    {
        get => SelectedCharacter?.CognitiveArchitectureEnabled ?? false;
        set
        {
            if (SelectedCharacter is null || SelectedCharacter.CognitiveArchitectureEnabled == value) return;
            SelectedCharacter.CognitiveArchitectureEnabled = value;
            OnPropertyChanged(nameof(SelectedCharacterCognitiveArchitectureEnabled));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }
    public string SelectedCharacterCognitiveStatus =>
        CognitiveStatusText.For(SelectedCharacter, SelectedCharacterCognitiveArchitectureEnabled);

    public bool SelectedCharacterSoulMemoryEnabled
    {
        get => SelectedCharacter?.SoulMemoryEnabled ?? false;
        set
        {
            if (SelectedCharacter is null || SelectedCharacter.SoulMemoryEnabled == value) return;
            SelectedCharacter.SoulMemoryEnabled = value;
            OnPropertyChanged(nameof(SelectedCharacterSoulMemoryEnabled));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }
    public string SelectedCharacterSoulMemoryPreset
    {
        get => SoulMemoryPresetMode.From(SelectedCharacter?.SoulMemoryPreset).Id;
        set
        {
            if (SelectedCharacter is null) return;
            var preset = SoulMemoryPresetMode.From(value).Id;
            if (SelectedCharacter.SoulMemoryPreset == preset) return;
            SelectedCharacter.SoulMemoryPreset = preset;
            OnPropertyChanged(nameof(SelectedCharacterSoulMemoryPreset));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }
    public int SelectedCharacterSoulMemoryIntervalMessages
    {
        get => Math.Clamp(SelectedCharacter?.SoulMemoryIntervalMessages ?? 4, 1, 50);
        set
        {
            if (SelectedCharacter is null) return;
            var interval = Math.Clamp(value, 1, 50);
            if (SelectedCharacter.SoulMemoryIntervalMessages == interval) return;
            SelectedCharacter.SoulMemoryIntervalMessages = interval;
            OnPropertyChanged(nameof(SelectedCharacterSoulMemoryIntervalMessages));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }
    public bool SelectedCharacterAutoSummaryEnabled
    {
        get => SelectedCharacter?.AutoSummaryEnabled ?? false;
        set
        {
            if (SelectedCharacter is null || SelectedCharacter.AutoSummaryEnabled == value) return;
            SelectedCharacter.AutoSummaryEnabled = value;
            OnPropertyChanged(nameof(SelectedCharacterAutoSummaryEnabled));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }
    public int SelectedCharacterAutoSummaryIntervalMessages
    {
        get => Math.Clamp(SelectedCharacter?.AutoSummaryIntervalMessages ?? 5, 1, 100);
        set
        {
            if (SelectedCharacter is null) return;
            var interval = Math.Clamp(value, 1, 100);
            if (SelectedCharacter.AutoSummaryIntervalMessages == interval) return;
            SelectedCharacter.AutoSummaryIntervalMessages = interval;
            OnPropertyChanged(nameof(SelectedCharacterAutoSummaryIntervalMessages));
            OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        }
    }

    public bool CognitiveSoulMemoryEnabled
    {
        get => _cognitiveSoulMemoryEnabled;
        set
        {
            if (!Set(ref _cognitiveSoulMemoryEnabled, value)) return;
            OnPropertyChanged(nameof(CognitiveArchitectureStatus));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public string SelectedSoulMemoryPreset
    {
        get => _selectedSoulMemoryPreset;
        set
        {
            var preset = SoulMemoryPresetMode.From(value).Id;
            if (!Set(ref _selectedSoulMemoryPreset, preset)) return;
            OnPropertyChanged(nameof(SoulMemoryPresetDescription));
            OnPropertyChanged(nameof(CognitiveArchitectureStatus));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public string SoulMemoryPresetDescription => SoulMemoryPresetMode.From(SelectedSoulMemoryPreset).Description;
    public int CognitiveMemoryIntervalMessages
    {
        get => _cognitiveMemoryIntervalMessages;
        set
        {
            var interval = Math.Clamp(value, 1, 50);
            if (!Set(ref _cognitiveMemoryIntervalMessages, interval)) return;
            OnPropertyChanged(nameof(CognitiveArchitectureStatus));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public bool CognitiveAutoSummaryEnabled
    {
        get => _cognitiveAutoSummaryEnabled;
        set
        {
            if (!Set(ref _cognitiveAutoSummaryEnabled, value)) return;
            OnPropertyChanged(nameof(CognitiveArchitectureStatus));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public int CognitiveSummaryIntervalMessages
    {
        get => _cognitiveSummaryIntervalMessages;
        set
        {
            var interval = Math.Clamp(value, 1, 100);
            if (!Set(ref _cognitiveSummaryIntervalMessages, interval)) return;
            OnPropertyChanged(nameof(CognitiveArchitectureStatus));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public string CognitiveArchitectureStatus => $"Soul Memory: {(CognitiveSoulMemoryEnabled ? $"{SoulMemoryPresetMode.From(SelectedSoulMemoryPreset).DisplayName}, каждые {CognitiveMemoryIntervalMessages} реплик диалога" : "выключена")}; summary: {(CognitiveAutoSummaryEnabled ? $"каждые {CognitiveSummaryIntervalMessages} реплик диалога" : "выключено")}.";
    public string CognitiveBackgroundMode
    {
        get => _cognitiveBackgroundMode;
        set
        {
            // Automatic maintenance must not take the model away from the next chat turn.
            var mode = BackgroundModes.Idle;
            if (!Set(ref _cognitiveBackgroundMode, mode)) return;
            OnPropertyChanged(nameof(IsCognitiveIdleMode));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public bool IsCognitiveIdleMode => CognitiveBackgroundMode == BackgroundModes.Idle;
    public int CognitiveBackgroundIdleSeconds
    {
        get => _cognitiveBackgroundIdleSeconds;
        set
        {
            var seconds = Math.Clamp(value, 60, 300);
            if (!Set(ref _cognitiveBackgroundIdleSeconds, seconds)) return;
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public string CognitiveBackgroundStatus { get => _cognitiveBackgroundStatus; private set => Set(ref _cognitiveBackgroundStatus, value); }
    public string ModelSearchQuery { get => _modelSearchQuery; set { if (Set(ref _modelSearchQuery, value)) SearchModelsCommand.RaiseCanExecuteChanged(); } }
    public string ModelDownloadStatus { get => _modelDownloadStatus; private set => Set(ref _modelDownloadStatus, value); }
    public bool IsModelDownloadInProgress
    {
        get => _isModelDownloadInProgress;
        private set
        {
            if (!Set(ref _isModelDownloadInProgress, value)) return;
            PauseModelDownloadCommand.RaiseCanExecuteChanged();
            ResumeModelDownloadCommand.RaiseCanExecuteChanged();
            ToggleModelDownloadCommand.RaiseCanExecuteChanged();
            CancelModelDownloadCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ModelDownloadPauseResumeText));
            OnPropertyChanged(nameof(CanCancelModelDownload));
        }
    }
    public bool CanResumeModelDownload
    {
        get => _canResumeModelDownload;
        private set
        {
            if (!Set(ref _canResumeModelDownload, value)) return;
            ResumeModelDownloadCommand.RaiseCanExecuteChanged();
            ToggleModelDownloadCommand.RaiseCanExecuteChanged();
            CancelModelDownloadCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(ModelDownloadPauseResumeText));
            OnPropertyChanged(nameof(CanCancelModelDownload));
        }
    }
    public string ModelDownloadPauseResumeText => IsModelDownloadInProgress ? "Пауза" : CanResumeModelDownload ? "Продолжить" : "Пауза";
    public bool CanCancelModelDownload => IsModelDownloadInProgress || CanResumeModelDownload;
    public string GatewayQuery { get => _gatewayQuery; set { if (Set(ref _gatewayQuery, value)) SearchGatewayCommand.RaiseCanExecuteChanged(); } }
    public string GatewayCategory
    {
        get => _gatewayCategory;
        set
        {
            var category = GatewayCategories.Any(x => x.Id == value) ? value : "soul";
            if (!Set(ref _gatewayCategory, category)) return;
            OnPropertyChanged(nameof(GatewayCategoryTitle));
            OnPropertyChanged(nameof(GatewayCategorySubtitle));
            OnPropertyChanged(nameof(ShowGatewayNsfw));
            _ = SaveGatewayPreferencesAsync();
            if (CurrentPage == "Gateway" && !IsBusy) _ = LoadGatewayAsync();
        }
    }
    public bool GatewayNsfwEnabled
    {
        get => _gatewayNsfwEnabled;
        set
        {
            if (!Set(ref _gatewayNsfwEnabled, value)) return;
            _ = SaveGatewayPreferencesAsync();
            if (GatewayCategory == "chub" && CurrentPage == "Gateway" && !IsBusy) _ = LoadGatewayAsync();
        }
    }
    public bool ShowGatewayNsfw => GatewayCategory == "chub";
    public string GatewayCategoryTitle => GatewayCategories.FirstOrDefault(x => x.Id == GatewayCategory)?.Title ?? "Characters Gateway";
    public string GatewayCategorySubtitle => GatewayCategories.FirstOrDefault(x => x.Id == GatewayCategory)?.Description ?? "Публичные материалы для локального текста.";
    public bool GatewayHasMore { get => _gatewayHasMore; private set { if (Set(ref _gatewayHasMore, value)) LoadMoreGatewayCommand.RaiseCanExecuteChanged(); } }
    public GatewayAssetItem? SelectedGatewayAsset { get => _selectedGatewayAsset; set { if (Set(ref _selectedGatewayAsset, value)) ImportGatewayAssetCommand.RaiseCanExecuteChanged(); } }
    public ModelHubSearchResult? SelectedModelResult
    {
        get => _selectedModelResult;
        set
        {
            if (!Set(ref _selectedModelResult, value)) return;
            _ = LoadModelFilesAsync(value);
            _ = LoadModelDetailsAsync(value);
        }
    }
    public ModelHubDetails? SelectedModelDetails { get => _selectedModelDetails; private set => Set(ref _selectedModelDetails, value); }
    public ModelHubFile? SelectedModelFile { get => _selectedModelFile; set { if (Set(ref _selectedModelFile, value)) DownloadSelectedModelCommand.RaiseCanExecuteChanged(); } }
    public SoulModelInstallation? SelectedInstalledModel
    {
        get => _selectedInstalledModel;
        set
        {
            if (!Set(ref _selectedInstalledModel, value)) return;
            UseInstalledModelCommand.RaiseCanExecuteChanged();
            ToggleModelStartStopCommand.RaiseCanExecuteChanged();
            _ = SelectInstalledModelAsync(value);
        }
    }
    public RecommendedModel? SelectedRecommendedModel
    {
        get => _selectedRecommendedModel;
        set
        {
            if (!Set(ref _selectedRecommendedModel, value)) return;
            OnPropertyChanged(nameof(RecommendedModelDownloadText));
            DownloadRecommendedModelCommand.RaiseCanExecuteChanged();
            SetupDownloadRecommendedCommand.RaiseCanExecuteChanged();
            _ = LoadRecommendedModelSizeAsync(value);
        }
    }
    public string RecommendedModelDownloadText => SelectedRecommendedModel?.IsInstalled == true ? "Уже скачана" : "Скачать рекомендуемый квант";
    public LlamaBackendOption? SelectedLlamaBackend
    {
        get => _selectedLlamaBackend;
        set
        {
            if (!Set(ref _selectedLlamaBackend, value) || value is null) return;
            LlamaOptions.EngineBackend = value.Id;
            SetupInstallEngineCommand.RaiseCanExecuteChanged();
            SetupSelectAndInstallBackendCommand.RaiseCanExecuteChanged();
            NextInitialSetupStepCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanInstallSelectedBackend));
            OnPropertyChanged(nameof(SelectedBackendDescription));
            OnPropertyChanged(nameof(SelectedBackendInstallState));
        }
    }
    public string SelectedBackendDescription => SelectedLlamaBackend?.Description ?? "Выберите вариант вычислений для llama.cpp.";
    public string SelectedBackendInstallState => SelectedLlamaBackend is null ? "Не выбран" : _installer.IsBackendInstalled(SelectedLlamaBackend.Id) ? "Установлен локально" : "Не установлен";
    public bool CanInstallSelectedBackend => !IsBusy && SelectedLlamaBackend is not null && !_installer.IsBackendInstalled(SelectedLlamaBackend.Id);
    public bool HasInstalledModels => InstalledModels.Count > 0;
    public SoulLorebook? SelectedLorebook
    {
        get => _selectedLorebook;
        set
        {
            if (Set(ref _selectedLorebook, value))
            {
                RefreshLorebookBindingFlag();
                DeleteLoreEntryCommand.RaiseCanExecuteChanged();
                SaveLorebookCommand.RaiseCanExecuteChanged();
                AddLoreEntryCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsLibraryLoreEditorOpen { get => _isLibraryLoreEditorOpen; set => Set(ref _isLibraryLoreEditorOpen, value); }

    public bool IsSelectedLorebookBound
    {
        get => _isSelectedLorebookBound;
        set { if (Set(ref _isSelectedLorebookBound, value)) _ = SetLorebookBindingAsync(value); }
    }
    public string MobileAccessUsername { get => _mobileAccessUsername; set => Set(ref _mobileAccessUsername, value?.Trim() ?? ""); }
    public string MobileAccessPassword { get => _mobileAccessPassword; set => Set(ref _mobileAccessPassword, value ?? ""); }
    public bool StartMobileServerOnLaunch
    {
        get => _startMobileServerOnLaunch;
        set => Set(ref _startMobileServerOnLaunch, value);
    }
    public string NetworkAddress => $"http://{GetLocalIp()}:{MobileServerPort}/";
    public string NetworkAccessUrl => NetworkAddress;
    public string NetworkAccessToken => "Вход выполняется по заданным ниже логину и паролю.";
    public string ModelState => IsBusy && !IsModelRunning ? "Загрузка модели…" : IsModelRunning ? "Модель работает" : "Не запущена";
    public bool IsModelRunning => _llama.IsStartedByApplication || _isModelApiAvailable;
    public string ModelStartStopText => IsModelRunning ? "■  Остановить" : "▶  Запустить";
    public string ModelLaunchDiagnostic => _llama.LastLaunchDiagnostic;
    public string ModelLaunchCommand => _llama.LastLaunchCommand;
    public bool NetworkRunning => _network.IsRunning;
    public string ModelSourceText => !string.IsNullOrWhiteSpace(ModelRepository) ? $"Репозиторий: {ModelRepository}" : "Источник модели: локальный GGUF-файл";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void NormalizeDiscreteGenerationLimits() => LlamaSettingsFactory.NormalizeDiscreteLimits(LlamaOptions);



}
