using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows;
using SoulTextWpf.Models;
using SoulTextWpf.Services;

namespace SoulTextWpf.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MobileServerPort = 8000;
    private readonly CharacterLibraryService _library = AppServices.CharacterLibrary;
    private readonly JsonDataStore _store = AppServices.DataStore;
    private readonly LlamaServerService _llama = new();
    private readonly LlamaInstallerService _installer = new();
    private readonly ModelsHubService _modelsHub = AppServices.ModelsHub;
    private readonly RecommendedModelsService _recommendedModels = AppServices.RecommendedModels;
    private readonly PromptEngine _promptEngine = AppServices.PromptEngine;
    private readonly LorebookService _lorebooks = AppServices.Lorebooks;
    private readonly PersonaService _personas = AppServices.Personas;
    private readonly StateVariableService _stateVariables = AppServices.StateVariables;
    private readonly CharacterCardImportService _characterCards = AppServices.CharacterCards;
    private readonly CharactersGatewayService _charactersGateway = AppServices.CharactersGateway;
    private readonly SoulOfWaifuImportService _soulOfWaifuImporter = AppServices.SoulOfWaifuImporter;
    private readonly CharacterCardExportService _characterCardExporter = AppServices.CharacterCardExporter;
    private readonly SceneService _scenes = AppServices.Scenes;
    private readonly ScenePromptEngine _scenePromptEngine = AppServices.ScenePromptEngine;
    private readonly ConversationTurnRunner _conversationTurnRunner = new(AppServices.Scenes, AppServices.ScenePromptEngine);
    private readonly NetworkChatServer _network;
    private readonly CognitiveBackgroundScheduler _cognitiveScheduler;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _networkSceneLoops = new();
    private SoulCharacter? _selectedCharacter;
    private SoulChat? _selectedChat;
    private ChatListItemViewModel? _selectedChatListItem;
    private string _draft = "";
    private string _optionsTab = "llm";
    private ChatAppearanceSettings _chatAppearance = new();
    private bool _isAssistantTyping;
    private string _status = "Подготовка…";
    private string _serverPath = "";
    private string _modelPath = "";
    private string _modelRepository = "";
    private string _modelSearchQuery = "Qwen";
    private string _modelDownloadStatus = "";
    private ModelHubSearchResult? _selectedModelResult;
    private ModelHubDetails? _selectedModelDetails;
    private ModelHubFile? _selectedModelFile;
    private SoulLorebook? _selectedLorebook;
    private bool _isLibraryLoreEditorOpen;
    private bool _isSelectedLorebookBound;
    private SoulPersona? _selectedPersona;
    private bool _isPersonaEditorOpen;
    private SoulPersona? _personaPendingDeletion;
    private bool _isBusy;
    private string _gatewayQuery = "";
    private GatewayAssetItem? _selectedGatewayAsset;
    private string _gatewayCategory = "soul";
    private bool _gatewayNsfwEnabled;
    private int _gatewayPage = 1;
    private bool _gatewayHasMore = true;
    private string _currentPage = "Home";
    private string _modelsHubTab = "Recommendations";
    private SoulModelInstallation? _selectedInstalledModel;
    private LlamaBackendOption? _selectedLlamaBackend;
    private RecommendedModel? _selectedRecommendedModel;
    private bool _isInitialSetupVisible;
    private int _initialSetupStep = 1;
    private double _setupProgressPercent;
    private double _setupModelDownloadPercent;
    private string _setupProgressText = "Выберите backend для установки.";
    private bool _setupModelDownloaded;
    private string _recommendedModelSizeInfo = "Выберите модель, чтобы получить размер рекомендуемого GGUF-кванта.";
    private CancellationTokenSource? _modelDownloadCts;
    private ModelDownloadRequest? _resumableDownload;
    private bool _isModelDownloadInProgress;
    private bool _canResumeModelDownload;
    private bool _downloadPauseRequested;
    private bool _downloadCancelRequested;
    private string _chatCharacterSortMode = "recent";
    private string _chatSearchQuery = "";
    private ConversationListItemViewModel? _selectedConversationItem;
    private string _newConversationType = "chat";
    private bool _isNewChatCharacterPickerOpen;
    private SoulCharacter? _newChatCharacter;
    private string _newChatNameDraft = "Новый чат";
    private bool _isChatActionMenuOpen;
    private ChatListItemViewModel? _chatActionMenuItem;
    private bool _isRenameChatDialogOpen;
    private ChatListItemViewModel? _renameChatItem;
    private string _renameChatNameDraft = "";
    private bool _isRenameSceneDialogOpen;
    private SoulScene? _renameScene;
    private string _renameSceneNameDraft = "";
    private bool _isMessageActionMenuOpen;
    private ChatMessageViewModel? _messageActionMenuItem;
    private bool _isChatMessageSearchOpen;
    private string _chatMessageSearchQuery = "";
    private ChatMessageSearchResult? _selectedChatMessageSearchResult;
    private bool _isSceneMessageSearchOpen;
    private string _sceneMessageSearchQuery = "";
    private ChatMessageSearchResult? _selectedSceneMessageSearchResult;
    private bool _isCharacterDescriptionExpanded;
    private bool _isCharacterPersonalityExpanded;
    private bool _isCharacterScenarioExpanded;
    private string _homeCharacterSortMode = "recent";
    private string _libraryTab = "characters";
    private bool _cognitiveSoulMemoryEnabled = true;
    private string _selectedSoulMemoryPreset = "full";
    private int _cognitiveMemoryIntervalMessages = 4;
    private bool _cognitiveAutoSummaryEnabled = true;
    private int _cognitiveSummaryIntervalMessages = 5;
    private string _cognitiveBackgroundMode = "idle";
    private int _cognitiveBackgroundIdleSeconds = 60;
    private string _cognitiveBackgroundStatus = "Фоновые обновления памяти готовы.";
    private string _mobileAccessUsername = "admin";
    private string _mobileAccessPassword = "admin";
    private bool _startMobileServerOnLaunch;
    // API may already be running before this ViewModel owns a Process instance.
    // Keep UI state in sync with successful health checks as well as owned processes.
    private bool _isModelApiAvailable;
    private string _characterGenerationIdea = "";
    private bool _isCharacterGeneratorOpen;
    private bool _isCharacterCreationDialogOpen;
    private string _characterCreationMode = "";
    private string _characterNameDraft = "";
    private SoulCharacter? _characterPendingDeletion;
    private string _characterEditorTab = "info";
    private SoulScene? _selectedScene;
    private SoulCharacter? _sceneCharacterA;
    private SoulCharacter? _sceneCharacterB;
    private string _sceneNameDraft = "Очередь за попкорном";
    private string _sceneScenarioDraft = "Два персонажа стоят в очереди за попкорном перед вечерним сеансом и начинают непринуждённый разговор.";
    private string _sceneLocationDraft = "Фойе кинотеатра";
    private string _sceneTimeDraft = "Вечер перед началом фильма";
    private string _sceneMoodDraft = "Лёгкое любопытство";
    private string _sceneGoalDraft = "Познакомиться и обсудить фильм";
    private string _sceneRelationshipDraft = "";
    private string _sceneTurnModeDraft = "alternate";
    private int _sceneDelaySecondsDraft = 10;
    private bool _sceneEnforceContractDraft = true;
    private bool _sceneAdvanceNarrativeDraft = true;
    private string _sceneDirectorDraft = "";
    private bool _isSceneGenerating;
    private bool _isSceneTyping;
    private bool _isSceneComposerOpen;
    private string _sceneRunStatus = "Создайте сцену или выберите сохранённую.";
    private int _sceneCountdownSeconds;
    private CancellationTokenSource? _sceneTimerCts;
    private int _networkRefreshQueued;


    private MainViewModel()
    {
        Characters = new ObservableCollection<SoulCharacter>();
        HomeCards = new ObservableCollection<HomeCharacterCardViewModel>();
        HomeCharacterSortOptions = new ObservableCollection<ChatCharacterSortOption>(
        [
            new("recent", "По дате последней реплики"),
            new("count", "По количеству реплик"),
            new("created", "По дате создания"),
            new("name", "По алфавиту")
        ]);
        ChatCharacters = new ObservableCollection<SoulCharacter>();
        ChatListItems = new ObservableCollection<ChatListItemViewModel>();
        ConversationItems = new ObservableCollection<ConversationListItemViewModel>();
        ChatMessageSearchResults = new ObservableCollection<ChatMessageSearchResult>();
        SceneMessageSearchResults = new ObservableCollection<ChatMessageSearchResult>();
        ChatCharacterSortOptions = new ObservableCollection<ChatCharacterSortOption>(
        [
            new("recent", "По дате"),
            new("name", "По имени")
        ]);
        Chats = new ObservableCollection<SoulChat>();
        Messages = new ObservableCollection<ChatMessageViewModel>();
        ModelSearchResults = new ObservableCollection<ModelHubSearchResult>();
        ModelFiles = new ObservableCollection<ModelHubFile>();
        InstalledModels = new ObservableCollection<SoulModelInstallation>();
        RecommendedModels = new ObservableCollection<RecommendedModel>();
        LlamaBackends = new ObservableCollection<LlamaBackendOption>(_installer.AvailableBackends);
        SoulMemoryPresets = new ObservableCollection<SoulMemoryPresetMode>(SoulMemoryPresetMode.All);
        PromptPresetOptions = new ObservableCollection<PromptPresetOption>();
        Lorebooks = new ObservableCollection<SoulLorebook>();
        Personas = new ObservableCollection<SoulPersona>();
        GatewayItems = new ObservableCollection<GatewayAssetItem>();
        GatewayCategories = new ObservableCollection<GatewayCategoryOption>(
        [
            new("soul", "Soul Gateway", "Готовые Character Card V2 с карточкой и доступным лором."),
            new("chub", "Chub AI Hub", "Публичные готовые персонажи из Characters Gateway."),
            new("lorebooks", "World Lorebooks", "Миры, правила, места и события для привязки к персонажу."),
            new("scenarios", "Текстовые сценарии", "Сценарии оригинала, адаптированные в обычный чат без Soul Stage.")
        ]);
        StateVariableValues = new ObservableCollection<StateVariableContextItem>();
        Scenes = new ObservableCollection<SoulScene>();
        SceneMessages = new ObservableCollection<SceneMessageViewModel>();
        _network = new NetworkChatServer(AskFromNetworkAsync, () => Characters, ControlSceneFromNetworkAsync, () => (MobileAccessUsername, MobileAccessPassword), GenerateCharacterFromNetworkAsync, ExpandCharacterFieldFromNetworkAsync, RefreshDesktopAfterNetworkMutationAsync);
        _cognitiveScheduler = new CognitiveBackgroundScheduler(ReportCognitiveBackground);
        NavigateCommand = new RelayCommand(page => NavigateTo(page as string ?? "Chat"));
        SelectLibraryTabCommand = new RelayCommand(value => LibraryTab = value as string ?? "characters");
        SetModelsHubTabCommand = new RelayCommand(tab => SetModelsHubTab(tab as string ?? "Recommendations"));
        SelectCharacterEditorTabCommand = new RelayCommand(tab => CharacterEditorTab = tab as string ?? "info");

        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null && !string.IsNullOrWhiteSpace(Draft));
        ContinueChatCommand = new AsyncRelayCommand(_ => ContinueChatAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null);
        StartModelCommand = new AsyncRelayCommand(_ => StartModelAsync(), _ => !IsBusy);
        StopModelCommand = new AsyncRelayCommand(_ => StopModelAsync(), _ => !IsBusy && _llama.IsStartedByApplication);
        ToggleModelStartStopCommand = new AsyncRelayCommand(_ => ToggleModelStartStopAsync(), _ => !IsBusy && (_llama.IsStartedByApplication || SelectedInstalledModel is not null || !string.IsNullOrWhiteSpace(ModelPath)));
        InstallEngineCommand = new AsyncRelayCommand(_ => InstallEngineAsync(), _ => !IsBusy);
        UseStarterModelCommand = new AsyncRelayCommand(_ => UseStarterModelAsync(), _ => !IsBusy);
        ToggleNetworkCommand = new AsyncRelayCommand(_ => ToggleNetworkAsync(), _ => !IsBusy);
        CopyNetworkAddressCommand = new RelayCommand(_ => CopyNetworkAddress());
        AddCharacterCommand = new AsyncRelayCommand(_ => AddCharacterAsync(), _ => !IsBusy);
        ToggleCharacterGeneratorCommand = new RelayCommand(_ => IsCharacterGeneratorOpen = !IsCharacterGeneratorOpen, _ => !IsBusy);
        OpenCharacterCreationDialogCommand = new RelayCommand(_ => OpenCharacterCreationDialog(), _ => !IsBusy);
        SelectCharacterCreationModeCommand = new RelayCommand(value => SelectCharacterCreationMode(value as string), _ => !IsBusy);
        CloseCharacterCreationDialogCommand = new RelayCommand(_ => CloseCharacterCreationDialog());
        CreateCharacterWithNameCommand = new AsyncRelayCommand(_ => CreateCharacterWithNameAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(CharacterNameDraft));
        GenerateCharacterFromIdeaCommand = new AsyncRelayCommand(_ => GenerateCharacterFromIdeaAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(CharacterGenerationIdea));
        ImportCharacterCommand = new AsyncRelayCommand(_ => ImportCharacterAsync(), _ => !IsBusy);
        ImportSoulOfWaifuCommand = new AsyncRelayCommand(_ => ImportSoulOfWaifuAsync(), _ => !IsBusy);
        ExportCharacterCommand = new AsyncRelayCommand(_ => ExportCharacterAsync(), _ => !IsBusy && SelectedCharacter is not null);
        DeleteCharacterCommand = new AsyncRelayCommand(_ => DeleteCharacterAsync(), _ => !IsBusy && Characters.Count > 1 && SelectedCharacter is not null);
        OpenCharacterChatCommand = new AsyncRelayCommand(x => OpenCharacterChatAsync(x as SoulCharacter), _ => !IsBusy);
        OpenCharacterEditorCommand = new AsyncRelayCommand(x => OpenCharacterEditorAsync(x as SoulCharacter), _ => !IsBusy);
        ConfirmDeleteCharacterCommand = new AsyncRelayCommand(x => ConfirmDeleteCharacterAsync(x as SoulCharacter), _ => !IsBusy && Characters.Count > 1);
        ConfirmCharacterDeleteCommand = new AsyncRelayCommand(_ => ConfirmCharacterDeleteAsync(), _ => !IsBusy && CharacterPendingDeletion is not null);
        CancelCharacterDeleteCommand = new RelayCommand(_ => CharacterPendingDeletion = null);
        AddChatCommand = new AsyncRelayCommand(_ => OpenNewChatCharacterPickerAsync(), _ => !IsBusy && Characters.Count > 0);
        ConfirmNewChatForCharacterCommand = new AsyncRelayCommand(_ => CreateNewConversationAsync(), _ => !IsBusy && (IsNewSceneType ? SceneCharacterA is not null && SceneCharacterB is not null && SceneCharacterA.Id != SceneCharacterB.Id : NewChatCharacter is not null));
        CancelNewChatCharacterPickerCommand = new RelayCommand(_ => { IsNewChatCharacterPickerOpen = false; NewChatNameDraft = "Новый чат"; NewConversationType = "chat"; });
        ToggleChatPinnedCommand = new AsyncRelayCommand(x => ToggleChatPinnedAsync(x as ChatListItemViewModel), _ => !IsBusy);
        ToggleConversationPinnedCommand = new AsyncRelayCommand(x => ToggleConversationPinnedAsync(x as ConversationListItemViewModel), _ => !IsBusy);
        BeginRenameConversationCommand = new RelayCommand(x => BeginRenameConversation(x as ConversationListItemViewModel), _ => !IsBusy);
        DeleteConversationCommand = new AsyncRelayCommand(x => DeleteConversationAsync(x as ConversationListItemViewModel), _ => !IsBusy);
        ConfirmRenameSceneCommand = new AsyncRelayCommand(_ => ConfirmRenameSceneAsync(), _ => !IsBusy && RenameScene is not null && !string.IsNullOrWhiteSpace(RenameSceneNameDraft));
        CancelRenameSceneCommand = new RelayCommand(_ => CloseRenameSceneDialog());
        OpenChatActionMenuCommand = new RelayCommand(x => OpenChatActionMenu(x as ChatListItemViewModel));
        CloseChatActionMenuCommand = new RelayCommand(_ => CloseChatActionMenu());
        ConfirmRenameChatCommand = new AsyncRelayCommand(_ => ConfirmRenameChatAsync(), _ => !IsBusy && RenameChatItem is not null && !string.IsNullOrWhiteSpace(RenameChatNameDraft));
        CancelRenameChatDialogCommand = new RelayCommand(_ => CloseRenameChatDialog());
        OpenMessageActionMenuCommand = new RelayCommand(x => OpenMessageActionMenu(x as ChatMessageViewModel));
        CloseMessageActionMenuCommand = new RelayCommand(_ => IsMessageActionMenuOpen = false);
        ToggleChatMessageSearchCommand = new RelayCommand(_ => IsChatMessageSearchOpen = !IsChatMessageSearchOpen, _ => SelectedChat is not null);
        CloseChatMessageSearchCommand = new RelayCommand(_ => IsChatMessageSearchOpen = false);
        SelectChatMessageSearchResultCommand = new RelayCommand(x => SelectChatMessageSearchResult(x as ChatMessageSearchResult));
        ToggleSceneMessageSearchCommand = new RelayCommand(_ => IsSceneMessageSearchOpen = !IsSceneMessageSearchOpen, _ => SelectedScene is not null);
        CloseSceneMessageSearchCommand = new RelayCommand(_ => IsSceneMessageSearchOpen = false);
        SelectSceneMessageSearchResultCommand = new RelayCommand(x => SelectSceneMessageSearchResult(x as ChatMessageSearchResult));
        ToggleCharacterCardSectionCommand = new RelayCommand(x => ToggleCharacterCardSection(x as string));
        DeleteChatCommand = new AsyncRelayCommand(_ => DeleteChatAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null);
        CreateChatForCharacterCommand = new AsyncRelayCommand(x => CreateChatForCharacterAsync(x as ChatListItemViewModel), _ => !IsBusy);
        DeleteChatListItemCommand = new AsyncRelayCommand(x => DeleteChatListItemAsync(x as ChatListItemViewModel), _ => !IsBusy);
        BeginRenameChatCommand = new RelayCommand(x => BeginRenameChat(x as ChatListItemViewModel), x => !IsBusy && x is ChatListItemViewModel);
        SaveRenameChatCommand = new AsyncRelayCommand(x => SaveRenameChatAsync(x as ChatListItemViewModel), x => !IsBusy && x is ChatListItemViewModel item && item.IsRenaming);
        CancelRenameChatCommand = new RelayCommand(x => CancelRenameChat(x as ChatListItemViewModel), x => x is ChatListItemViewModel item && item.IsRenaming);
        ChooseServerCommand = new RelayCommand(_ => ChooseServer());
        ChooseModelCommand = new AsyncRelayCommand(_ => ChooseModelAsync(), _ => !IsBusy);
        ChooseAvatarCommand = new RelayCommand(_ => ChooseAvatar());
        SaveCharacterCommand = new AsyncRelayCommand(_ => SaveCharacterAsync(), _ => !IsBusy && SelectedCharacter is not null);
        SaveChatStartingContextCommand = new AsyncRelayCommand(_ => SaveChatStartingContextAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null);
        ExpandCharacterFieldCommand = new AsyncRelayCommand(value => ExpandCharacterFieldAsync(value as string), _ => !IsBusy && SelectedCharacter is not null);
        PreviousVariantCommand = new AsyncRelayCommand(x => ShiftVariantAsync(x as ChatMessageViewModel, -1), x => x is ChatMessageViewModel message && message.CanMovePrevious && SelectedCharacter is not null && SelectedChat is not null);
        NextVariantCommand = new AsyncRelayCommand(x => ShiftVariantAsync(x as ChatMessageViewModel, 1), x => x is ChatMessageViewModel message && message.CanMoveNext && SelectedCharacter is not null && SelectedChat is not null);
        BeginEditMessageCommand = new RelayCommand(x => BeginMessageEdit(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel && SelectedCharacter is not null && SelectedChat is not null);
        CancelEditMessageCommand = new RelayCommand(x => CancelMessageEdit(x as ChatMessageViewModel), x => x is ChatMessageViewModel message && message.IsEditing);
        SaveEditMessageCommand = new AsyncRelayCommand(x => SaveMessageEditAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel message && message.IsEditing && SelectedCharacter is not null && SelectedChat is not null);
        DeleteMessageCommand = new AsyncRelayCommand(x => DeleteMessageAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel && SelectedCharacter is not null && SelectedChat is not null);
        ContinueFromMessageCommand = new AsyncRelayCommand(x => ContinueFromMessageAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel message && message.CanContinueFromHere && SelectedCharacter is not null && SelectedChat is not null);
        CreateSceneCommand = new AsyncRelayCommand(_ => CreateSceneAsync(), _ => !IsBusy && SceneCharacterA is not null && SceneCharacterB is not null && SceneCharacterA.Id != SceneCharacterB.Id);
        BeginCreateSceneCommand = new RelayCommand(_ => BeginCreateScene(), _ => !IsBusy);
        CancelCreateSceneCommand = new RelayCommand(_ => { IsSceneComposerOpen = false; });
        SaveSceneCommand = new AsyncRelayCommand(_ => SaveSceneAsync(), _ => !IsBusy && SelectedScene is not null);
        DeleteSceneCommand = new AsyncRelayCommand(_ => DeleteSceneAsync(), _ => !IsBusy && SelectedScene is not null);
        StartSceneCommand = new AsyncRelayCommand(_ => StartSceneAsync(), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating);
        PauseSceneCommand = new AsyncRelayCommand(_ => PauseSceneAsync(), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating);
        ToggleSceneStartPauseCommand = new AsyncRelayCommand(_ => ToggleSceneStartPauseAsync(), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating);
        NextSceneTurnCommand = new AsyncRelayCommand(_ => GenerateNextSceneTurnAsync(), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating);
        ChooseSceneSpeakerCommand = new AsyncRelayCommand(value => ChooseSceneSpeakerAsync(value as SoulCharacter), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating);
        AddDirectorEventCommand = new AsyncRelayCommand(_ => AddDirectorEventAsync(), _ => !IsBusy && SelectedScene is not null && !string.IsNullOrWhiteSpace(SceneDirectorDraft));
        FinishSceneCommand = new AsyncRelayCommand(_ => FinishSceneAsync(), _ => !IsBusy && SelectedScene is not null && !IsSceneGenerating && SelectedScene.Status != "finished");
        SearchModelsCommand = new AsyncRelayCommand(_ => SearchModelsAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(ModelSearchQuery));
        DownloadSelectedModelCommand = new AsyncRelayCommand(_ => DownloadSelectedModelAsync(), _ => !IsBusy && SelectedModelResult is not null && SelectedModelFile is not null);
        LoadRecommendedModelsCommand = new AsyncRelayCommand(_ => LoadRecommendedModelsAsync(true), _ => !IsModelDownloadInProgress);
        DownloadRecommendedModelCommand = new AsyncRelayCommand(_ => DownloadRecommendedModelAsync(), _ => !IsBusy && SelectedRecommendedModel is not null && !SelectedRecommendedModel.IsInstalled);
        PauseModelDownloadCommand = new RelayCommand(_ => PauseModelDownload(), _ => IsModelDownloadInProgress);
        ResumeModelDownloadCommand = new AsyncRelayCommand(_ => ResumeModelDownloadAsync(), _ => !IsBusy && CanResumeModelDownload && !IsModelDownloadInProgress);
        ToggleModelDownloadCommand = new RelayCommand(_ => ToggleModelDownload(), _ => IsModelDownloadInProgress || CanResumeModelDownload);
        CancelModelDownloadCommand = new RelayCommand(_ => CancelModelDownload(), _ => CanCancelModelDownload);
        RefreshInstalledModelsCommand = new AsyncRelayCommand(_ => RefreshInstalledModelsAsync(), _ => !IsBusy);
        UseInstalledModelCommand = new AsyncRelayCommand(_ => UseInstalledModelAsync(), _ => !IsBusy && SelectedInstalledModel is not null);
        SaveModelSettingsCommand = new AsyncRelayCommand(_ => SaveModelSettingsAsync(), _ => !IsBusy);
        SaveChatAppearanceCommand = new AsyncRelayCommand(_ => SaveChatAppearanceAsync(), _ => !IsBusy);
        ResetChatAppearanceCommand = new RelayCommand(_ => ResetChatAppearance());
        SelectOptionsTabCommand = new RelayCommand(value => SelectOptionsTab(value as string));
        SetChatAppearanceColorCommand = new RelayCommand(value => SetChatAppearanceColor(value as string));
        AddLorebookCommand = new AsyncRelayCommand(_ => AddLorebookAsync(), _ => !IsBusy);
        OpenLibraryLoreEditorCommand = new RelayCommand(value => OpenLibraryLoreEditor(value as SoulLorebook));
        CloseLibraryLoreEditorCommand = new RelayCommand(_ => IsLibraryLoreEditorOpen = false);
        DeleteLoreEntryCommand = new AsyncRelayCommand(value => DeleteLoreEntryAsync(value as SoulLoreEntry), _ => !IsBusy && SelectedLorebook is not null);
        DeleteLorebookCommand = new AsyncRelayCommand(value => DeleteLorebookAsync(value as SoulLorebook), _ => !IsBusy);
        SaveLorebookCommand = new AsyncRelayCommand(_ => SaveLorebookAsync(), _ => !IsBusy && SelectedLorebook is not null);
        AddLoreEntryCommand = new AsyncRelayCommand(_ => AddLoreEntryAsync(), _ => !IsBusy && SelectedLorebook is not null);
        AddPersonaCommand = new AsyncRelayCommand(_ => AddPersonaAsync(), _ => !IsBusy);
        OpenPersonaEditorCommand = new RelayCommand(value => OpenPersonaEditor(value as SoulPersona), _ => !IsBusy);
        ClosePersonaEditorCommand = new RelayCommand(_ => IsPersonaEditorOpen = false);
        SavePersonaCommand = new AsyncRelayCommand(_ => SavePersonaAsync(), _ => !IsBusy && SelectedPersona is not null);
        ConfirmDeletePersonaCommand = new RelayCommand(value => ConfirmDeletePersona(value as SoulPersona), _ => !IsBusy);
        DeletePersonaCommand = new AsyncRelayCommand(_ => DeletePersonaAsync(), _ => !IsBusy && PersonaPendingDeletion is not null);
        CancelPersonaDeleteCommand = new RelayCommand(_ => PersonaPendingDeletion = null);
        ChoosePersonaAvatarCommand = new RelayCommand(_ => ChoosePersonaAvatar(), _ => !IsBusy && SelectedPersona is not null);
        LoadGatewayTrendingCommand = new AsyncRelayCommand(_ => { GatewayCategory = "chub"; return LoadGatewayAsync(); }, _ => !IsBusy);
        SearchGatewayCommand = new AsyncRelayCommand(_ => LoadGatewayAsync(), _ => !IsBusy);
        SetGatewayCategoryCommand = new RelayCommand(x => GatewayCategory = x as string ?? "soul");
        ImportGatewayAssetCommand = new AsyncRelayCommand(_ => ImportGatewayAssetAsync(), _ => !IsBusy && SelectedGatewayAsset is not null && !SelectedGatewayAsset.IsAlreadyImported);
        LoadMoreGatewayCommand = new AsyncRelayCommand(_ => LoadGatewayAsync(append: true), _ => !IsBusy && GatewayHasMore);
        UpdateMemoryCommand = new AsyncRelayCommand(_ => UpdateCurrentMemoryAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null);
        UpdateSummaryCommand = new AsyncRelayCommand(_ => UpdateCurrentSummaryAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedChat is not null);
        SetupInstallEngineCommand = new AsyncRelayCommand(_ => InstallInitialEngineAsync(), _ => CanInstallSelectedBackend);
        SetupSelectAndInstallBackendCommand = new AsyncRelayCommand(x => SelectAndInstallInitialBackendAsync(x as string), _ => !IsBusy);
        SetupDownloadRecommendedCommand = new AsyncRelayCommand(_ => DownloadInitialRecommendedModelAsync(), _ => !IsBusy && SelectedRecommendedModel is not null && !SelectedRecommendedModel.IsInstalled);
        SkipInitialSetupCommand = new AsyncRelayCommand(_ => FinishInitialSetupAsync(), _ => !IsBusy);
        NextInitialSetupStepCommand = new AsyncRelayCommand(_ => MoveToModelStepAsync(), _ => !IsBusy && _installer.IsBackendInstalled(LlamaOptions.EngineBackend));
        PreviousInitialSetupStepCommand = new RelayCommand(_ =>
        {
            InitialSetupStep = 1;
            PreviousInitialSetupStepCommand?.RaiseCanExecuteChanged();
            NextInitialSetupStepCommand.RaiseCanExecuteChanged();
        }, _ => !IsBusy && IsInitialSetupModelStep);
        SetupStartChatCommand = new AsyncRelayCommand(_ => StartFromSetupAsync(), _ => !IsBusy && SetupModelDownloaded && !string.IsNullOrWhiteSpace(ModelPath));
    }

    public static async Task<MainViewModel> CreateAsync()
    {
        var viewModel = new MainViewModel();
        await viewModel.LoadAsync();
        await viewModel.StartNetworkOnLaunchAsync();
        return viewModel;
    }

    public ObservableCollection<SoulCharacter> Characters { get; }
    public ObservableCollection<HomeCharacterCardViewModel> HomeCards { get; }
    public ObservableCollection<ChatCharacterSortOption> HomeCharacterSortOptions { get; }
    public ObservableCollection<SoulCharacter> ChatCharacters { get; }
    public ObservableCollection<ChatListItemViewModel> ChatListItems { get; }
    public ObservableCollection<ConversationListItemViewModel> ConversationItems { get; }
    public ObservableCollection<ChatCharacterSortOption> ChatCharacterSortOptions { get; }
    public ObservableCollection<SoulChat> Chats { get; }
    public ObservableCollection<ChatMessageViewModel> Messages { get; }
    public ObservableCollection<ChatMessageSearchResult> ChatMessageSearchResults { get; }
    public ObservableCollection<ChatMessageSearchResult> SceneMessageSearchResults { get; }
    public ObservableCollection<SoulScene> Scenes { get; }
    public ObservableCollection<SceneMessageViewModel> SceneMessages { get; }
    public ObservableCollection<ModelHubSearchResult> ModelSearchResults { get; }
    public ObservableCollection<ModelHubFile> ModelFiles { get; }
    public ObservableCollection<SoulModelInstallation> InstalledModels { get; }
    public ObservableCollection<PromptPresetOption> PromptPresetOptions { get; }
    public ObservableCollection<RecommendedModel> RecommendedModels { get; }
    public ObservableCollection<LlamaBackendOption> LlamaBackends { get; }
    public ObservableCollection<SoulMemoryPresetMode> SoulMemoryPresets { get; }
    public LlamaRuntimeOptions LlamaOptions { get; } = new();
    public ChatAppearanceSettings ChatAppearance { get => _chatAppearance; private set => Set(ref _chatAppearance, value); }
    public bool IsLlmOptionsTab => string.Equals(_optionsTab, "llm", StringComparison.OrdinalIgnoreCase);
    public bool IsAppearanceOptionsTab => string.Equals(_optionsTab, "appearance", StringComparison.OrdinalIgnoreCase);
    public bool IsMobileOptionsTab => string.Equals(_optionsTab, "mobile", StringComparison.OrdinalIgnoreCase);
    public string ChatAppearancePreviewText => "*Она медленно прищурилась и улыбнулась.* «Я помню достаточно». **Главное**: `status: active`.";
    public ObservableCollection<SoulLorebook> Lorebooks { get; }
    public ObservableCollection<SoulPersona> Personas { get; }
    public ObservableCollection<GatewayAssetItem> GatewayItems { get; }
    public ObservableCollection<GatewayCategoryOption> GatewayCategories { get; }
    public ObservableCollection<StateVariableContextItem> StateVariableValues { get; }

    public AsyncRelayCommand SendCommand { get; }
    public AsyncRelayCommand StartModelCommand { get; }
    public AsyncRelayCommand StopModelCommand { get; }
    public AsyncRelayCommand ToggleModelStartStopCommand { get; }
    public AsyncRelayCommand InstallEngineCommand { get; }
    public AsyncRelayCommand UseStarterModelCommand { get; }
    public AsyncRelayCommand ToggleNetworkCommand { get; }
    public RelayCommand CopyNetworkAddressCommand { get; }
    public AsyncRelayCommand AddCharacterCommand { get; }
    public RelayCommand ToggleCharacterGeneratorCommand { get; }
    public RelayCommand OpenCharacterCreationDialogCommand { get; }
    public RelayCommand SelectCharacterCreationModeCommand { get; }
    public RelayCommand CloseCharacterCreationDialogCommand { get; }
    public AsyncRelayCommand CreateCharacterWithNameCommand { get; }
    public AsyncRelayCommand GenerateCharacterFromIdeaCommand { get; }
    public AsyncRelayCommand ImportCharacterCommand { get; }
    public AsyncRelayCommand ImportSoulOfWaifuCommand { get; }
    public AsyncRelayCommand ExportCharacterCommand { get; }
    public AsyncRelayCommand DeleteCharacterCommand { get; }
    public AsyncRelayCommand OpenCharacterChatCommand { get; }
    public AsyncRelayCommand OpenCharacterEditorCommand { get; }
    public AsyncRelayCommand ConfirmDeleteCharacterCommand { get; }
    public AsyncRelayCommand ConfirmCharacterDeleteCommand { get; }
    public RelayCommand CancelCharacterDeleteCommand { get; }
    public AsyncRelayCommand AddChatCommand { get; }
    public AsyncRelayCommand ConfirmNewChatForCharacterCommand { get; }
    public RelayCommand CancelNewChatCharacterPickerCommand { get; }
    public AsyncRelayCommand ToggleChatPinnedCommand { get; }
    public AsyncRelayCommand ToggleConversationPinnedCommand { get; }
    public RelayCommand BeginRenameConversationCommand { get; }
    public AsyncRelayCommand DeleteConversationCommand { get; }
    public AsyncRelayCommand ConfirmRenameSceneCommand { get; }
    public RelayCommand CancelRenameSceneCommand { get; }
    public RelayCommand OpenChatActionMenuCommand { get; }
    public RelayCommand CloseChatActionMenuCommand { get; }
    public AsyncRelayCommand ConfirmRenameChatCommand { get; }
    public RelayCommand CancelRenameChatDialogCommand { get; }
    public RelayCommand OpenMessageActionMenuCommand { get; }
    public RelayCommand CloseMessageActionMenuCommand { get; }
    public RelayCommand ToggleChatMessageSearchCommand { get; }
    public RelayCommand CloseChatMessageSearchCommand { get; }
    public RelayCommand SelectChatMessageSearchResultCommand { get; }
    public RelayCommand ToggleSceneMessageSearchCommand { get; }
    public RelayCommand CloseSceneMessageSearchCommand { get; }
    public RelayCommand SelectSceneMessageSearchResultCommand { get; }
    public RelayCommand ToggleCharacterCardSectionCommand { get; }
    public AsyncRelayCommand DeleteChatCommand { get; }
    public AsyncRelayCommand CreateChatForCharacterCommand { get; }
    public AsyncRelayCommand DeleteChatListItemCommand { get; }
    public RelayCommand BeginRenameChatCommand { get; }
    public AsyncRelayCommand SaveRenameChatCommand { get; }
    public RelayCommand CancelRenameChatCommand { get; }
    public AsyncRelayCommand SaveCharacterCommand { get; }
    public AsyncRelayCommand ExpandCharacterFieldCommand { get; }
    public AsyncRelayCommand PreviousVariantCommand { get; }
    public AsyncRelayCommand NextVariantCommand { get; }
    public RelayCommand BeginEditMessageCommand { get; }
    public RelayCommand CancelEditMessageCommand { get; }
    public AsyncRelayCommand SaveEditMessageCommand { get; }
    public AsyncRelayCommand DeleteMessageCommand { get; }
    public AsyncRelayCommand ContinueFromMessageCommand { get; }
    public AsyncRelayCommand SearchModelsCommand { get; }
    public AsyncRelayCommand DownloadSelectedModelCommand { get; }
    public AsyncRelayCommand LoadRecommendedModelsCommand { get; }
    public AsyncRelayCommand DownloadRecommendedModelCommand { get; }
    public RelayCommand PauseModelDownloadCommand { get; }
    public AsyncRelayCommand ResumeModelDownloadCommand { get; }
    public RelayCommand ToggleModelDownloadCommand { get; }
    public RelayCommand CancelModelDownloadCommand { get; }
    public AsyncRelayCommand RefreshInstalledModelsCommand { get; }
    public AsyncRelayCommand UseInstalledModelCommand { get; }
    public AsyncRelayCommand SaveModelSettingsCommand { get; }
    public AsyncRelayCommand SaveChatAppearanceCommand { get; }
    public RelayCommand ResetChatAppearanceCommand { get; }
    public RelayCommand SelectOptionsTabCommand { get; }
    public RelayCommand SetChatAppearanceColorCommand { get; }
    public AsyncRelayCommand AddLorebookCommand { get; }
    public RelayCommand OpenLibraryLoreEditorCommand { get; }
    public RelayCommand CloseLibraryLoreEditorCommand { get; }
    public AsyncRelayCommand DeleteLoreEntryCommand { get; }
    public AsyncRelayCommand DeleteLorebookCommand { get; }
    public AsyncRelayCommand SaveLorebookCommand { get; }
    public AsyncRelayCommand AddLoreEntryCommand { get; }
    public AsyncRelayCommand AddPersonaCommand { get; }
    public RelayCommand OpenPersonaEditorCommand { get; }
    public RelayCommand ClosePersonaEditorCommand { get; }
    public AsyncRelayCommand SavePersonaCommand { get; }
    public RelayCommand ConfirmDeletePersonaCommand { get; }
    public AsyncRelayCommand DeletePersonaCommand { get; }
    public RelayCommand CancelPersonaDeleteCommand { get; }
    public RelayCommand ChoosePersonaAvatarCommand { get; }
    public AsyncRelayCommand LoadGatewayTrendingCommand { get; }
    public AsyncRelayCommand SearchGatewayCommand { get; }
    public RelayCommand SetGatewayCategoryCommand { get; }
    public AsyncRelayCommand ImportGatewayAssetCommand { get; }
    public AsyncRelayCommand LoadMoreGatewayCommand { get; }
    public AsyncRelayCommand UpdateMemoryCommand { get; }
    public AsyncRelayCommand UpdateSummaryCommand { get; }
    public AsyncRelayCommand SetupInstallEngineCommand { get; }
    public AsyncRelayCommand SetupSelectAndInstallBackendCommand { get; }
    public AsyncRelayCommand SetupDownloadRecommendedCommand { get; }
    public AsyncRelayCommand SkipInitialSetupCommand { get; }
    public AsyncRelayCommand NextInitialSetupStepCommand { get; }
    public RelayCommand PreviousInitialSetupStepCommand { get; }
    public AsyncRelayCommand SetupStartChatCommand { get; }
    public AsyncRelayCommand CreateSceneCommand { get; }
    public RelayCommand BeginCreateSceneCommand { get; }
    public RelayCommand CancelCreateSceneCommand { get; }
    public AsyncRelayCommand SaveSceneCommand { get; }
    public AsyncRelayCommand DeleteSceneCommand { get; }
    public AsyncRelayCommand StartSceneCommand { get; }
    public AsyncRelayCommand PauseSceneCommand { get; }
    public AsyncRelayCommand ToggleSceneStartPauseCommand { get; }
    public AsyncRelayCommand NextSceneTurnCommand { get; }
    public AsyncRelayCommand ChooseSceneSpeakerCommand { get; }
    public AsyncRelayCommand AddDirectorEventCommand { get; }
    public AsyncRelayCommand FinishSceneCommand { get; }
    public RelayCommand SelectCharacterEditorTabCommand { get; }
    public AsyncRelayCommand ContinueChatCommand { get; }
    public AsyncRelayCommand SaveChatStartingContextCommand { get; }
    public RelayCommand NavigateCommand { get; }
    public RelayCommand SelectLibraryTabCommand { get; }
    public RelayCommand SetModelsHubTabCommand { get; }
    public RelayCommand ChooseServerCommand { get; }
    public AsyncRelayCommand ChooseModelCommand { get; }
    public RelayCommand ChooseAvatarCommand { get; }

    public SoulCharacter? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (Set(ref _selectedCharacter, value))
            {
                OnPropertyChanged(nameof(SelectedCharacterCognitiveArchitectureEnabled));
                OnPropertyChanged(nameof(SelectedCharacterSoulMemoryEnabled));
                OnPropertyChanged(nameof(SelectedCharacterSoulMemoryPreset));
                OnPropertyChanged(nameof(SelectedCharacterSoulMemoryIntervalMessages));
                OnPropertyChanged(nameof(SelectedCharacterAutoSummaryEnabled));
                OnPropertyChanged(nameof(SelectedCharacterAutoSummaryIntervalMessages));
                OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
                OnPropertyChanged(nameof(SelectedCharacterPromptPresetId));
                OnPropertyChanged(nameof(SelectedPromptPresetDescription));
                OnPropertyChanged(nameof(SelectedCharacterPersonaId));
                OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
                OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
                RaiseChatPresentationProperties();
                ContinueChatCommand.RaiseCanExecuteChanged();
                _ = SelectCharacterAsync(value);
            }
        }
    }

    public Guid? SelectedCharacterPromptPresetId
    {
        get => SelectedCharacter?.SelectedPromptPresetId;
        set
        {
            if (SelectedCharacter is null || SelectedCharacter.SelectedPromptPresetId == value) return;
            SelectedCharacter.SelectedPromptPresetId = value;
            OnPropertyChanged(nameof(SelectedCharacterPromptPresetId));
            OnPropertyChanged(nameof(SelectedPromptPresetDescription));
        }
    }

    public string SelectedPromptPresetDescription => PromptPresetOptions.FirstOrDefault(option => option.Id == SelectedCharacter?.SelectedPromptPresetId)?.Description
        ?? "Используется только системный промпт, карточка персонажа, лорбук, Summary и Soul Memory без дополнительного режима ведения диалога.";

    public Guid? SelectedCharacterPersonaId
    {
        get => SelectedCharacter?.SelectedPersonaId;
        set
        {
            if (SelectedCharacter is null) return;
            var resolved = value is Guid personaId && Personas.Any(persona => persona.Id == personaId) ? value : null;
            if (SelectedCharacter.SelectedPersonaId == resolved) return;
            SelectedCharacter.SelectedPersonaId = resolved;
            OnPropertyChanged(nameof(SelectedCharacterPersonaId));
            OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
            OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
        }
    }

    public bool IsSelectedCharacterPersonaEnabled
    {
        get => SelectedCharacterPersonaId is not null;
        set
        {
            if (SelectedCharacter is null) return;
            if (!value)
            {
                SelectedCharacterPersonaId = null;
                OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
                return;
            }

            if (SelectedCharacterPersonaId is null)
                SelectedCharacterPersonaId = Personas.FirstOrDefault()?.Id;
            OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        }
    }

    public string SelectedCharacterPersonaDescription => Personas.FirstOrDefault(persona => persona.Id == SelectedCharacterPersonaId)?.Description
        ?? "Выберите персону из библиотеки. Её имя и описание будут добавляться в системный контекст этого персонажа.";

    public SoulPersona? SelectedPersona
    {
        get => _selectedPersona;
        set
        {
            if (!Set(ref _selectedPersona, value)) return;
            SavePersonaCommand.RaiseCanExecuteChanged();
            ChoosePersonaAvatarCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsPersonaEditorOpen { get => _isPersonaEditorOpen; set => Set(ref _isPersonaEditorOpen, value); }

    public SoulPersona? PersonaPendingDeletion
    {
        get => _personaPendingDeletion;
        set
        {
            if (!Set(ref _personaPendingDeletion, value)) return;
            OnPropertyChanged(nameof(IsPersonaDeleteDialogOpen));
            DeletePersonaCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsPersonaDeleteDialogOpen => PersonaPendingDeletion is not null;
    public bool HasPersonas => Personas.Count > 0;

    public SoulChat? SelectedChat
    {
        get => _selectedChat;
        set
        {
            if (Set(ref _selectedChat, value))
            {
                IsChatMessageSearchOpen = false;
                ChatMessageSearchQuery = "";
                RaiseChatPresentationProperties();
                ToggleChatMessageSearchCommand.RaiseCanExecuteChanged();
                ContinueChatCommand.RaiseCanExecuteChanged();
                _ = SelectChatAsync(value);
            }
        }
    }

    public SoulScene? SelectedScene
    {
        get => _selectedScene;
        set
        {
            if (!Set(ref _selectedScene, value)) return;
            CancelSceneTimer();
            if (value is not null) IsSceneComposerOpen = false;
            IsSceneMessageSearchOpen = false;
            SceneMessageSearchQuery = "";
            _ = LoadSelectedSceneAsync(value?.Id);
            OnPropertyChanged(nameof(SelectedSceneCharacterA));
            OnPropertyChanged(nameof(SelectedSceneCharacterB));
            OnPropertyChanged(nameof(SceneParticipants));
            OnPropertyChanged(nameof(SceneParticipantNames));
            OnPropertyChanged(nameof(SceneStartPauseText));
            OnPropertyChanged(nameof(IsSceneFinished));
            OnPropertyChanged(nameof(IsSceneConversationVisible));
            OnPropertyChanged(nameof(IsSceneCountdownVisible));
            OnPropertyChanged(nameof(SceneLastMessageLabel));
            ToggleSceneMessageSearchCommand.RaiseCanExecuteChanged();
            RaiseSceneCommands();
        }
    }
    public SoulCharacter? SceneCharacterA
    {
        get => _sceneCharacterA;
        set
        {
            if (!Set(ref _sceneCharacterA, value)) return;
            CreateSceneCommand.RaiseCanExecuteChanged();
            ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();
        }
    }
    public SoulCharacter? SceneCharacterB
    {
        get => _sceneCharacterB;
        set
        {
            if (!Set(ref _sceneCharacterB, value)) return;
            CreateSceneCommand.RaiseCanExecuteChanged();
            ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();
        }
    }
    public string SceneNameDraft { get => _sceneNameDraft; set => Set(ref _sceneNameDraft, value); }
    public string SceneScenarioDraft { get => _sceneScenarioDraft; set => Set(ref _sceneScenarioDraft, value); }
    public string SceneLocationDraft { get => _sceneLocationDraft; set => Set(ref _sceneLocationDraft, value); }
    public string SceneTimeDraft { get => _sceneTimeDraft; set => Set(ref _sceneTimeDraft, value); }
    public string SceneMoodDraft { get => _sceneMoodDraft; set => Set(ref _sceneMoodDraft, value); }
    public string SceneGoalDraft { get => _sceneGoalDraft; set => Set(ref _sceneGoalDraft, value); }
    public string SceneRelationshipDraft { get => _sceneRelationshipDraft; set => Set(ref _sceneRelationshipDraft, value); }
    public string SceneTurnModeDraft { get => _sceneTurnModeDraft; set => Set(ref _sceneTurnModeDraft, value == "manual" ? "manual" : "alternate"); }
    public int SceneDelaySecondsDraft { get => _sceneDelaySecondsDraft; set => Set(ref _sceneDelaySecondsDraft, Math.Clamp(value, 0, 30)); }
    public bool SceneEnforceContractDraft { get => _sceneEnforceContractDraft; set => Set(ref _sceneEnforceContractDraft, value); }
    public bool SceneAdvanceNarrativeDraft { get => _sceneAdvanceNarrativeDraft; set => Set(ref _sceneAdvanceNarrativeDraft, value); }
    public string SceneDirectorDraft { get => _sceneDirectorDraft; set { if (Set(ref _sceneDirectorDraft, value)) AddDirectorEventCommand.RaiseCanExecuteChanged(); } }
    public bool IsSceneGenerating { get => _isSceneGenerating; private set { if (Set(ref _isSceneGenerating, value)) RaiseSceneCommands(); } }
    public bool IsSceneTyping { get => _isSceneTyping; private set => Set(ref _isSceneTyping, value); }
    public bool IsSceneComposerOpen
    {
        get => _isSceneComposerOpen;
        private set
        {
            if (Set(ref _isSceneComposerOpen, value))
            {
                OnPropertyChanged(nameof(IsSceneConversationVisible));
            }
        }
    }
    public bool IsSceneConversationVisible => SelectedScene is not null && !IsSceneComposerOpen;
    public bool IsSceneFinished => SelectedScene?.Status == "finished";
    public string SceneStartPauseText => SelectedScene?.Status == "running" ? "Пауза" : "Старт";
    public string SceneRunStatus { get => _sceneRunStatus; private set => Set(ref _sceneRunStatus, value); }
    public int SceneCountdownSeconds
    {
        get => _sceneCountdownSeconds;
        private set
        {
            if (!Set(ref _sceneCountdownSeconds, value)) return;
            OnPropertyChanged(nameof(SceneCountdownText));
            OnPropertyChanged(nameof(IsSceneCountdownVisible));
        }
    }
    public bool IsSceneCountdownVisible => SceneCountdownSeconds > 0 && SelectedScene?.Status == "running";
    public string SceneCountdownText => SceneCountdownSeconds > 0 ? $"Следующая реплика через {SceneCountdownSeconds} сек." : string.Empty;
    public string SceneLastMessageLabel
    {
        get
        {
            var message = SelectedScene?.Messages?.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.SequenceNumber).FirstOrDefault();
            return message is null
                ? "Последнее сообщение: пока нет"
                : $"Последнее сообщение · {message.CreatedAt.LocalDateTime:dd.MM.yyyy · HH:mm}";
        }
    }
    public string SceneNextSpeakerName
    {
        get
        {
            if (SelectedScene is null || SelectedScene.NextCharacterId is null) return "Выберите следующего говорящего";
            return Characters.FirstOrDefault(character => character.Id == SelectedScene.NextCharacterId)?.Name ?? "Персонаж";
        }
    }
    public bool IsSceneSelected => SelectedScene is not null;
    public SoulCharacter? SelectedSceneCharacterA => SelectedScene is null ? null : Characters.FirstOrDefault(character => character.Id == SelectedScene.CharacterAId);
    public SoulCharacter? SelectedSceneCharacterB => SelectedScene is null ? null : Characters.FirstOrDefault(character => character.Id == SelectedScene.CharacterBId);
    public IEnumerable<SoulCharacter> SceneParticipants => [.. new[] { SelectedSceneCharacterA, SelectedSceneCharacterB }.Where(character => character is not null)!];
    public string SceneParticipantNames => SelectedSceneCharacterA is null || SelectedSceneCharacterB is null ? "Участники не найдены" : $"{SelectedSceneCharacterA.Name} · {SelectedSceneCharacterB.Name}";

    public ChatListItemViewModel? SelectedChatListItem
    {
        get => _selectedChatListItem;
        set
        {
            if (!Set(ref _selectedChatListItem, value) || value is null) return;
            _ = OpenChatListItemAsync(value);
        }
    }

    public ConversationListItemViewModel? SelectedConversationItem
    {
        get => _selectedConversationItem;
        set
        {
            if (!Set(ref _selectedConversationItem, value) || value is null) return;
            OnPropertyChanged(nameof(IsSceneChatActive));
            _ = OpenConversationItemAsync(value);
        }
    }

    public bool IsSceneChatActive => SelectedConversationItem?.IsScene == true;

    public string NewConversationType
    {
        get => _newConversationType;
        set
        {
            var normalized = string.Equals(value, "scene", StringComparison.OrdinalIgnoreCase) ? "scene" : "chat";
            if (!Set(ref _newConversationType, normalized)) return;
            OnPropertyChanged(nameof(IsNewSceneType));
            OnPropertyChanged(nameof(NewConversationConfirmText));
            ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNewSceneType => string.Equals(NewConversationType, "scene", StringComparison.OrdinalIgnoreCase);
    public string NewConversationConfirmText => IsNewSceneType ? "Создать сцену" : "Создать чат";

    public string ChatCharacterSortMode
    {
        get => _chatCharacterSortMode;
        set
        {
            var mode = value == "name" ? "name" : "recent";
            if (!Set(ref _chatCharacterSortMode, mode)) return;
            RebuildChatCharacters();
        }
    }

    public string ChatSearchQuery
    {
        get => _chatSearchQuery;
        set
        {
            if (!Set(ref _chatSearchQuery, value)) return;
            RebuildChatCharacters();
        }
    }

    public bool IsNewChatCharacterPickerOpen
    {
        get => _isNewChatCharacterPickerOpen;
        set
        {
            if (!Set(ref _isNewChatCharacterPickerOpen, value)) return;
            ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();
        }
    }

    public SoulCharacter? NewChatCharacter
    {
        get => _newChatCharacter;
        set
        {
            if (!Set(ref _newChatCharacter, value)) return;
            ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewChatNameDraft
    {
        get => _newChatNameDraft;
        set => Set(ref _newChatNameDraft, value);
    }

    public bool IsChatActionMenuOpen
    {
        get => _isChatActionMenuOpen;
        set => Set(ref _isChatActionMenuOpen, value);
    }

    public ChatListItemViewModel? ChatActionMenuItem
    {
        get => _chatActionMenuItem;
        set => Set(ref _chatActionMenuItem, value);
    }

    public bool IsRenameChatDialogOpen
    {
        get => _isRenameChatDialogOpen;
        set => Set(ref _isRenameChatDialogOpen, value);
    }

    public ChatListItemViewModel? RenameChatItem
    {
        get => _renameChatItem;
        set
        {
            if (!Set(ref _renameChatItem, value)) return;
            ConfirmRenameChatCommand.RaiseCanExecuteChanged();
        }
    }

    public string RenameChatNameDraft
    {
        get => _renameChatNameDraft;
        set
        {
            if (!Set(ref _renameChatNameDraft, value)) return;
            ConfirmRenameChatCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsRenameSceneDialogOpen { get => _isRenameSceneDialogOpen; set => Set(ref _isRenameSceneDialogOpen, value); }
    public SoulScene? RenameScene
    {
        get => _renameScene;
        set
        {
            if (!Set(ref _renameScene, value)) return;
            ConfirmRenameSceneCommand.RaiseCanExecuteChanged();
        }
    }
    public string RenameSceneNameDraft
    {
        get => _renameSceneNameDraft;
        set
        {
            if (!Set(ref _renameSceneNameDraft, value)) return;
            ConfirmRenameSceneCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsMessageActionMenuOpen
    {
        get => _isMessageActionMenuOpen;
        set => Set(ref _isMessageActionMenuOpen, value);
    }

    public ChatMessageViewModel? MessageActionMenuItem
    {
        get => _messageActionMenuItem;
        set => Set(ref _messageActionMenuItem, value);
    }

    public bool IsChatMessageSearchOpen
    {
        get => _isChatMessageSearchOpen;
        set
        {
            if (!Set(ref _isChatMessageSearchOpen, value)) return;
            if (value) RefreshChatMessageSearchResults();
        }
    }

    public string ChatMessageSearchQuery
    {
        get => _chatMessageSearchQuery;
        set
        {
            if (!Set(ref _chatMessageSearchQuery, value)) return;
            RefreshChatMessageSearchResults();
        }
    }

    public ChatMessageSearchResult? SelectedChatMessageSearchResult
    {
        get => _selectedChatMessageSearchResult;
        set => Set(ref _selectedChatMessageSearchResult, value);
    }

    public bool IsSceneMessageSearchOpen
    {
        get => _isSceneMessageSearchOpen;
        set
        {
            if (!Set(ref _isSceneMessageSearchOpen, value)) return;
            if (value) RefreshSceneMessageSearchResults();
        }
    }
    public string SceneMessageSearchQuery
    {
        get => _sceneMessageSearchQuery;
        set
        {
            if (!Set(ref _sceneMessageSearchQuery, value)) return;
            RefreshSceneMessageSearchResults();
        }
    }
    public ChatMessageSearchResult? SelectedSceneMessageSearchResult
    {
        get => _selectedSceneMessageSearchResult;
        set => Set(ref _selectedSceneMessageSearchResult, value);
    }

    public string SelectedChatHeaderTitle => SelectedChat?.Name ?? "Выберите диалог";
    public string SelectedCharacterPresence => IsModelRunning ? "Онлайн" : "Локальный чат";
    public string SelectedChatLastMessageLabel
    {
        get
        {
            var message = SelectedChat?.Messages?.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.SequenceNumber).FirstOrDefault();
            return message is null
                ? "Последнее сообщение: пока нет"
                : $"Последнее сообщение · {message.CreatedAt.LocalDateTime:dd.MM.yyyy · HH:mm}";
        }
    }
    public string SelectedCharacterCreatedLabel => SelectedCharacter is null ? "—" : SelectedCharacter.CreatedAt.LocalDateTime.ToString("dd MMM yyyy");
    public int SelectedChatMessageCount => SelectedChat?.Messages?.Count ?? 0;
    public string SelectedCharacterTitle => SelectedCharacter?.Title?.Trim() ?? "";
    public bool IsCharacterDescriptionExpanded => _isCharacterDescriptionExpanded;
    public bool IsCharacterPersonalityExpanded => _isCharacterPersonalityExpanded;
    public bool IsCharacterScenarioExpanded => _isCharacterScenarioExpanded;
    public string SelectedCharacterDescriptionDisplay => CharacterCardText(SelectedCharacter?.Description, IsCharacterDescriptionExpanded);
    public string SelectedCharacterPersonalityDisplay => CharacterCardText(SelectedCharacter?.Personality, IsCharacterPersonalityExpanded);
    public string SelectedCharacterScenarioDisplay => CharacterCardText(SelectedCharacter?.Scenario, IsCharacterScenarioExpanded);
    public string CharacterDescriptionToggleText => IsCharacterDescriptionExpanded ? "Скрыть" : "Читать далее";
    public string CharacterPersonalityToggleText => IsCharacterPersonalityExpanded ? "Скрыть" : "Читать далее";
    public string CharacterScenarioToggleText => IsCharacterScenarioExpanded ? "Скрыть" : "Читать далее";
    public bool HasSelectedCharacterDescriptionOverflow => HasCardTextOverflow(SelectedCharacter?.Description);
    public bool HasSelectedCharacterPersonalityOverflow => HasCardTextOverflow(SelectedCharacter?.Personality);
    public bool HasSelectedCharacterScenarioOverflow => HasCardTextOverflow(SelectedCharacter?.Scenario);
    public IEnumerable<string> SelectedCharacterPersonalityTags => (SelectedCharacter?.Personality ?? "")
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(tag => tag.Length > 1)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .Take(7);
    public bool HasSelectedCharacterPersonalityTags => SelectedCharacterPersonalityTags.Any();

    public string LibraryTab
    {
        get => _libraryTab;
        set
        {
            var tab = string.Equals(value, "lore", StringComparison.OrdinalIgnoreCase)
                ? "lore"
                : string.Equals(value, "personas", StringComparison.OrdinalIgnoreCase) ? "personas" : "characters";
            if (!Set(ref _libraryTab, tab)) return;
            OnPropertyChanged(nameof(IsLibraryCharactersTab));
            OnPropertyChanged(nameof(IsLibraryLoreTab));
            OnPropertyChanged(nameof(IsLibraryPersonasTab));
        }
    }
    public bool IsLibraryCharactersTab => string.Equals(LibraryTab, "characters", StringComparison.OrdinalIgnoreCase);
    public bool IsLibraryLoreTab => string.Equals(LibraryTab, "lore", StringComparison.OrdinalIgnoreCase);
    public bool IsLibraryPersonasTab => string.Equals(LibraryTab, "personas", StringComparison.OrdinalIgnoreCase);

    public string HomeCharacterSortMode
    {
        get => _homeCharacterSortMode;
        set
        {
            var mode = value is "count" or "created" or "name" ? value : "recent";
            if (!Set(ref _homeCharacterSortMode, mode)) return;
            RebuildHomeCards();
        }
    }

    public string Draft
    {
        get => _draft;
        set { if (Set(ref _draft, value)) SendCommand.RaiseCanExecuteChanged(); }
    }

    public string CharacterEditorTab
    {
        get => _characterEditorTab;
        set
        {
            var tab = value is "memory" or "lore" ? value : "info";
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
        // Memory and lore now live inside the selected character card; scenes now live inside Chats.
        if (string.Equals(page, "Memory", StringComparison.OrdinalIgnoreCase)) page = "Characters";
        if (string.Equals(page, "Scene", StringComparison.OrdinalIgnoreCase)) page = "Chat";
        CurrentPage = page;
        if (page == "Gateway" && GatewayItems.Count == 0 && !IsBusy) _ = LoadGatewayAsync();
        if (page == "Models" && !IsBusy)
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
        ModelsHubTab = tab is "Catalog" or "Recommendations" or "Installed" ? tab : "Recommendations";
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

    public string PageTitle => CurrentPage switch
    {
        "Home" => "Библиотека",
        "Chat" => "Чаты",
        "Scene" => "СЦЕНА",
        "Characters" => "Карточка персонажа",
        "Gateway" => "ХАБ",
        "Models" => "Models Hub",
        "Memory" => "Память",
        "Mobile" => "Мобильный доступ",
        "Options" => "Параметры",
        "Setup" => "Быстрый старт",
        _ => "SoulExe"
    };

    public string PageSubtitle => CurrentPage switch
    {
        "Home" => "Персонажи и загруженные лорбуки",
        "Chat" => "Диалоги и история всех персонажей",
        "Scene" => "Самостоятельный диалог двух персонажей",
        "Characters" => "Настройка выбранного персонажа из Библиотеки",
        "Gateway" => "Каталог готовых персонажей, лорбуков и сценариев",
        "Models" => "Установка и выбор локальных GGUF-моделей",
        "Memory" => "Soul Memory и summary",
        "Mobile" => "Доступ с телефона в той же Wi‑Fi сети",
        "Options" => "Параметры локальной LLM и оборудования",
        "Setup" => "Движок llama.cpp и первая модель",
        _ => "Локальный текстовый AI"
    };
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
    public string SelectedCharacterCognitiveStatus => SelectedCharacter is null
        ? "Выберите персонажа."
        : !SelectedCharacterCognitiveArchitectureEnabled
            ? "Cognitive Architecture полностью отключена для этого персонажа; его память и summary не попадут в prompt."
            : $"Soul Memory: {(SelectedCharacter.SoulMemoryEnabled ? $"{SoulMemoryPresetMode.From(SelectedCharacter.SoulMemoryPreset).DisplayName}, каждые {SelectedCharacter.SoulMemoryIntervalMessages} реплик диалога" : "выключена")}; Auto-Summary: {(SelectedCharacter.AutoSummaryEnabled ? $"каждые {SelectedCharacter.AutoSummaryIntervalMessages} реплик диалога" : "выключено")}.";

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
            var mode = string.Equals(value, "immediate", StringComparison.OrdinalIgnoreCase) ? "immediate" : "idle";
            if (!Set(ref _cognitiveBackgroundMode, mode)) return;
            OnPropertyChanged(nameof(IsCognitiveIdleMode));
            _ = SaveCognitiveArchitectureAsync();
        }
    }
    public bool IsCognitiveIdleMode => CognitiveBackgroundMode == "idle";
    public int CognitiveBackgroundIdleSeconds
    {
        get => _cognitiveBackgroundIdleSeconds;
        set
        {
            var seconds = Math.Clamp(value, 10, 180);
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
        set
        {
            if (Set(ref _startMobileServerOnLaunch, value))
                _ = SaveMobileAccessAsync();
        }
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

    private static readonly int[] ContextSizeSteps = [2048, 4096, 8192, 16384, 32768, 65536, 131072];
    private static readonly int[] MaxTokensSteps = [128, 256, 512, 768, 1024, 1536, 2048, 3072, 4096, 8192, 16384, 32768, 65536];

    private static int SnapToStep(int value, IReadOnlyList<int> steps) => steps.OrderBy(step => Math.Abs(step - value)).ThenBy(step => step).First();

    private void NormalizeDiscreteGenerationLimits()
    {
        LlamaOptions.ContextSize = SnapToStep(LlamaOptions.ContextSize, ContextSizeSteps);
        LlamaOptions.MaxTokens = SnapToStep(LlamaOptions.MaxTokens, MaxTokensSteps);
    }

    private async Task LoadAsync()
    {
        var data = await _store.ReadAsync(root => root);
        if (data.Preferences.CognitiveMaintenancePolicyVersion < 2)
        {
            await _store.MutateAsync(root =>
            {
                // Earlier releases could begin a multi-call Full Soul Memory pipeline immediately
                // after an answer. Migrate that default to a real reading pause without disabling memory.
                if (string.Equals(root.Preferences.CognitiveBackgroundMode, "immediate", StringComparison.OrdinalIgnoreCase))
                    root.Preferences.CognitiveBackgroundMode = "idle";
                root.Preferences.CognitiveBackgroundIdleSeconds = Math.Max(60, root.Preferences.CognitiveBackgroundIdleSeconds);
                root.Preferences.CognitiveMaintenancePolicyVersion = 2;
            }, "migrate_cognitive_maintenance_v2");
            data = await _store.ReadAsync(root => root);
        }
        ServerPath = data.Preferences.LlamaServerPath;
        ModelPath = data.Preferences.ModelPath;
        ModelRepository = data.Preferences.ModelHuggingFaceRepository;
        _mobileAccessUsername = string.IsNullOrWhiteSpace(data.Preferences.MobileAccessUsername) ? "admin" : data.Preferences.MobileAccessUsername;
        _mobileAccessPassword = string.IsNullOrEmpty(data.Preferences.MobileAccessPassword) ? "admin" : data.Preferences.MobileAccessPassword;
        _startMobileServerOnLaunch = data.Preferences.LocalWebServerEnabled;
        OnPropertyChanged(nameof(MobileAccessUsername));
        OnPropertyChanged(nameof(MobileAccessPassword));
        OnPropertyChanged(nameof(StartMobileServerOnLaunch));
        _cognitiveSoulMemoryEnabled = data.Preferences.CognitiveSoulMemoryEnabled;
        _selectedSoulMemoryPreset = SoulMemoryPresetMode.From(data.Preferences.SoulMemoryPreset).Id;
        _cognitiveMemoryIntervalMessages = Math.Clamp(data.Preferences.CognitiveMemoryIntervalMessages, 1, 50);
        _cognitiveAutoSummaryEnabled = data.Preferences.CognitiveAutoSummaryEnabled;
        _cognitiveSummaryIntervalMessages = Math.Clamp(data.Preferences.CognitiveSummaryIntervalMessages, 1, 100);
        _cognitiveBackgroundMode = string.Equals(data.Preferences.CognitiveBackgroundMode, "immediate", StringComparison.OrdinalIgnoreCase) ? "immediate" : "idle";
        _cognitiveBackgroundIdleSeconds = Math.Clamp(data.Preferences.CognitiveBackgroundIdleSeconds, 10, 180);
        OnPropertyChanged(nameof(CognitiveSoulMemoryEnabled));
        OnPropertyChanged(nameof(SelectedSoulMemoryPreset));
        OnPropertyChanged(nameof(SoulMemoryPresetDescription));
        OnPropertyChanged(nameof(CognitiveMemoryIntervalMessages));
        OnPropertyChanged(nameof(CognitiveAutoSummaryEnabled));
        OnPropertyChanged(nameof(CognitiveSummaryIntervalMessages));
        OnPropertyChanged(nameof(CognitiveBackgroundMode));
        OnPropertyChanged(nameof(IsCognitiveIdleMode));
        OnPropertyChanged(nameof(CognitiveBackgroundIdleSeconds));
        OnPropertyChanged(nameof(CognitiveArchitectureStatus));
        _gatewayNsfwEnabled = data.Preferences.GatewayNsfwEnabled;
        _gatewayCategory = GatewayCategories.Any(x => x.Id == data.Preferences.GatewayCategory) ? data.Preferences.GatewayCategory : "soul";
        OnPropertyChanged(nameof(GatewayNsfwEnabled));
        OnPropertyChanged(nameof(GatewayCategory));
        OnPropertyChanged(nameof(GatewayCategoryTitle));
        OnPropertyChanged(nameof(GatewayCategorySubtitle));
        OnPropertyChanged(nameof(ShowGatewayNsfw));
        LoadLlamaOptions(data.Preferences);
        ChatAppearance = (data.Preferences.ChatAppearance ?? new ChatAppearanceSettings()).Clone();
        LoadPromptPresetOptions(data.PromptPresets);
        NormalizeDiscreteGenerationLimits();
        var existingSetup = _installer.IsBackendInstalled(data.Preferences.ActiveBackend) && !string.IsNullOrWhiteSpace(data.Preferences.ModelPath) && File.Exists(data.Preferences.ModelPath);
        var needsInitialSetup = !data.Preferences.InitialSetupCompleted && !existingSetup;
        IsInitialSetupVisible = false;
        InitialSetupStep = 1;
        CurrentPage = "Home";
        if (needsInitialSetup) _ = LoadRecommendedModelsAsync(false);
        RefreshBackendInstallStates();
        SelectedLlamaBackend = LlamaBackends.FirstOrDefault(x => string.Equals(x.Id, LlamaOptions.EngineBackend, StringComparison.OrdinalIgnoreCase)) ?? LlamaBackends.FirstOrDefault();
        await RefreshInstalledModelsAsync();
            await ReloadPersonasAsync();
            await ReloadCharactersAsync();
            await ReloadLorebooksAsync();
            await ReloadScenesAsync();
            RebuildConversationItems();
        Status = _installer.IsBackendInstalled(LlamaOptions.EngineBackend)
            ? $"Готово. Выбран движок {_installer.GetBackend(LlamaOptions.EngineBackend).DisplayName}."
            : "Библиотека персонажей готова. Установите выбранный backend llama.cpp перед первым диалогом.";
    }


    private void LoadPromptPresetOptions(IEnumerable<SoulPromptPreset>? presets)
    {
        PromptPresetOptions.Clear();
        PromptPresetOptions.Add(new PromptPresetOption(null, "Без пресета", "Используется только ваша карточка персонажа, системный промпт, лорбук, Summary и Soul Memory. Удобно, если вы хотите полностью собственные правила.", false));
        foreach (var preset in presets ?? [])
            PromptPresetOptions.Add(new PromptPresetOption(preset.Id, preset.Name, string.IsNullOrWhiteSpace(preset.Description) ? "Пользовательский системный пресет." : preset.Description, preset.IsBuiltIn));
        OnPropertyChanged(nameof(SelectedPromptPresetDescription));
    }

    private async Task ReloadCharactersAsync(Guid? selectId = null)
    {
        var characters = await _library.GetCharactersAsync();
        Characters.Clear();
        foreach (var character in characters) Characters.Add(character);
        RebuildHomeCards();
        var target = selectId is not null ? Characters.FirstOrDefault(x => x.Id == selectId) : Characters.FirstOrDefault();
        _selectedCharacter = target;
        RebuildChatCharacters();
        OnPropertyChanged(nameof(SelectedCharacter));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveArchitectureEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryPreset));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        OnPropertyChanged(nameof(SelectedCharacterPersonaId));
        OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
        await LoadChatsAsync();
        EnsureSceneDraftParticipants();
    }

    private void EnsureSceneDraftParticipants()
    {
        if (SceneCharacterA is null && Characters.Count > 0) SceneCharacterA = Characters[0];
        if (SceneCharacterB is null || SceneCharacterB.Id == SceneCharacterA?.Id)
            SceneCharacterB = Characters.FirstOrDefault(character => character.Id != SceneCharacterA?.Id);
    }

    private void RebuildHomeCards()
    {
        IEnumerable<SoulCharacter> ordered = HomeCharacterSortMode switch
        {
            "count" => Characters.OrderByDescending(CharacterMessageCount).ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            "created" => Characters.OrderByDescending(character => character.CreatedAt).ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            "name" => Characters.OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => Characters.OrderByDescending(CharacterLastActivity).ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        HomeCards.Clear();
        foreach (var character in ordered) HomeCards.Add(new HomeCharacterCardViewModel(character));
        HomeCards.Add(HomeCharacterCardViewModel.AddCard());
    }

    private static DateTimeOffset CharacterLastActivity(SoulCharacter character) =>
        character.Chats?.Where(chat => chat is not null && !chat.IsArchived)
            .SelectMany(chat => chat.Messages ?? [])
            .Select(message => message.CreatedAt)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max() ?? DateTimeOffset.MinValue;

    private static int CharacterMessageCount(SoulCharacter character) =>
        character.Chats?.Where(chat => chat is not null && !chat.IsArchived).Sum(chat => chat.Messages?.Count ?? 0) ?? 0;

    private void RefreshChatListItem(Guid characterId, Guid chatId)
    {
        foreach (var item in ChatListItems.Where(item => item.CharacterId == characterId && item.ChatId == chatId)) item.Refresh();
        if (SelectedCharacter?.Id == characterId && SelectedChat?.Id == chatId) RaiseChatPresentationProperties();
    }

    private void RebuildChatCharacters()
    {
        // The chat page works with individual conversations, while preserving the active pair.
        var selectedCharacterId = SelectedCharacter?.Id;
        var selectedChatId = SelectedChat?.Id;
        var entries = Characters.Where(character => character is not null)
            .SelectMany(character => (character.Chats ?? [])
                .Where(chat => chat is not null && !chat.IsArchived)
                .Select(chat => new ChatListItemViewModel(character, chat)));
        var query = ChatSearchQuery.Trim();
        if (!string.IsNullOrWhiteSpace(query))
            entries = entries.Where(item => item.MatchesSearch(query));

        var ordered = ChatCharacterSortMode == "name"
            ? entries.OrderByDescending(item => item.IsPinned)
                .ThenBy(item => item.CharacterName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChatName, StringComparer.CurrentCultureIgnoreCase)
            : entries.OrderByDescending(item => item.IsPinned)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.CharacterName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChatName, StringComparer.CurrentCultureIgnoreCase);

        ChatCharacters.Clear();
        foreach (var character in Characters.Where(character => character.Chats?.Any(chat => chat is not null && !chat.IsArchived) == true)) ChatCharacters.Add(character);
        ChatListItems.Clear();
        foreach (var item in ordered) ChatListItems.Add(item);

        var restored = selectedChatId is null ? null : ChatListItems.FirstOrDefault(item => item.ChatId == selectedChatId && item.CharacterId == selectedCharacterId);
        _selectedChatListItem = restored;
        OnPropertyChanged(nameof(SelectedChatListItem));
        OnPropertyChanged(nameof(ChatCharacters));
        OnPropertyChanged(nameof(ChatListItems));
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        RebuildConversationItems();
    }

    private void RebuildConversationItems()
    {
        var selectedKind = SelectedConversationItem?.IsScene == true ? "scene" : "chat";
        var selectedId = SelectedConversationItem?.Id ?? (SelectedChat?.Id ?? SelectedScene?.Id ?? Guid.Empty);
        var entries = new List<ConversationListItemViewModel>();
        entries.AddRange(Characters.Where(character => character is not null)
            .SelectMany(character => (character.Chats ?? [])
                .Where(chat => chat is not null && !chat.IsArchived)
                .Select(chat => ConversationListItemViewModel.FromChat(character, chat))));
        entries.AddRange(Scenes.Where(scene => scene is not null)
            .Select(scene => ConversationListItemViewModel.FromScene(scene,
                Characters.FirstOrDefault(character => character.Id == scene.CharacterAId),
                Characters.FirstOrDefault(character => character.Id == scene.CharacterBId))));
        var query = ChatSearchQuery.Trim();
        if (!string.IsNullOrWhiteSpace(query)) entries = entries.Where(item => item.MatchesSearch(query)).ToList();
        var ordered = ChatCharacterSortMode == "name"
            ? entries.OrderByDescending(item => item.IsPinned).ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.IsScene ? 1 : 0)
            : entries.OrderByDescending(item => item.IsPinned).ThenByDescending(item => item.UpdatedAt).ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase);
        ConversationItems.Clear();
        foreach (var item in ordered) ConversationItems.Add(item);
        _selectedConversationItem = ConversationItems.FirstOrDefault(item => item.Id == selectedId && (item.IsScene ? "scene" : "chat") == selectedKind)
            ?? ConversationItems.FirstOrDefault(item => item.IsScene && item.Id == SelectedScene?.Id)
            ?? ConversationItems.FirstOrDefault(item => !item.IsScene && item.Id == SelectedChat?.Id);
        OnPropertyChanged(nameof(SelectedConversationItem));
        OnPropertyChanged(nameof(ConversationItems));
        OnPropertyChanged(nameof(IsSceneChatActive));
    }

    private async Task OpenConversationItemAsync(ConversationListItemViewModel item)
    {
        if (item.IsScene)
        {
            var scene = Scenes.FirstOrDefault(value => value.Id == item.Id);
            if (scene is null) return;
            IsChatMessageSearchOpen = false;
            ChatMessageSearchQuery = "";
            SelectedScene = scene;
            CurrentPage = "Chat";
            OnPropertyChanged(nameof(IsSceneChatActive));
            return;
        }
        if (item.ChatItem is not null)
        {
            await OpenChatListItemAsync(item.ChatItem);
            OnPropertyChanged(nameof(IsSceneChatActive));
        }
    }

    private void RaiseChatPresentationProperties()
    {
        OnPropertyChanged(nameof(SelectedChatHeaderTitle));
        OnPropertyChanged(nameof(SelectedChatLastMessageLabel));
        OnPropertyChanged(nameof(SelectedCharacterPresence));
        OnPropertyChanged(nameof(SelectedCharacterCreatedLabel));
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        OnPropertyChanged(nameof(SelectedCharacterTitle));
        OnPropertyChanged(nameof(IsCharacterDescriptionExpanded));
        OnPropertyChanged(nameof(IsCharacterPersonalityExpanded));
        OnPropertyChanged(nameof(IsCharacterScenarioExpanded));
        OnPropertyChanged(nameof(SelectedCharacterDescriptionDisplay));
        OnPropertyChanged(nameof(SelectedCharacterPersonalityDisplay));
        OnPropertyChanged(nameof(SelectedCharacterScenarioDisplay));
        OnPropertyChanged(nameof(CharacterDescriptionToggleText));
        OnPropertyChanged(nameof(CharacterPersonalityToggleText));
        OnPropertyChanged(nameof(CharacterScenarioToggleText));
        OnPropertyChanged(nameof(HasSelectedCharacterDescriptionOverflow));
        OnPropertyChanged(nameof(HasSelectedCharacterPersonalityOverflow));
        OnPropertyChanged(nameof(HasSelectedCharacterScenarioOverflow));
        OnPropertyChanged(nameof(SelectedCharacterPersonalityTags));
        OnPropertyChanged(nameof(HasSelectedCharacterPersonalityTags));
    }

    private async Task SelectCharacterAsync(SoulCharacter? character)
    {
        if (character is null) return;
        await LoadChatsAsync();
        RefreshLorebookBindingFlag();
        RaiseAllCommands();
    }

    private async Task LoadChatsAsync()
    {
        Chats.Clear();
        Messages.Clear();
        if (SelectedCharacter is null) return;
        var fresh = await _library.GetCharacterAsync(SelectedCharacter.Id);
        if (fresh is null) return;
        var selectedIndex = Characters.IndexOf(SelectedCharacter);
        if (selectedIndex >= 0) Characters[selectedIndex] = fresh;
        _selectedCharacter = fresh;
        _isCharacterDescriptionExpanded = false;
        _isCharacterPersonalityExpanded = false;
        _isCharacterScenarioExpanded = false;
        OnPropertyChanged(nameof(SelectedCharacter));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveArchitectureEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryPreset));
        OnPropertyChanged(nameof(SelectedCharacterSoulMemoryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryEnabled));
        OnPropertyChanged(nameof(SelectedCharacterAutoSummaryIntervalMessages));
        OnPropertyChanged(nameof(SelectedCharacterCognitiveStatus));
        OnPropertyChanged(nameof(SelectedCharacterPersonaId));
        OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
        RaiseChatPresentationProperties();
        foreach (var chat in fresh.Chats.Where(x => !x.IsArchived).OrderByDescending(x => x.UpdatedAt)) Chats.Add(chat);
        _selectedChat = Chats.FirstOrDefault(x => x.Id == fresh.CurrentChatId) ?? Chats.FirstOrDefault();
        IsChatMessageSearchOpen = false;
        ChatMessageSearchQuery = "";
        RebuildChatCharacters();
        OnPropertyChanged(nameof(SelectedChat));
        ToggleChatMessageSearchCommand.RaiseCanExecuteChanged();
        ContinueChatCommand.RaiseCanExecuteChanged();
        RaiseChatPresentationProperties();
        LoadMessages();
        RefreshStateVariableValues();
    }

    private async Task SelectChatAsync(SoulChat? chat)
    {
        if (chat is null || SelectedCharacter is null) return;
        await _library.SelectChatAsync(SelectedCharacter.Id, chat.Id);
        LoadMessages();
        RefreshStateVariableValues();
        RaiseAllCommands();
    }

    private void LoadMessages()
    {
        Messages.Clear();
        if (SelectedChat is null) return;
        // При открытии длинного диалога показываем свежий фрагмент; полная история остаётся в JSON и поиске.
        DateOnly? previousDate = null;
        foreach (var message in SelectedChat.Messages.OrderBy(x => x.SequenceNumber).TakeLast(30))
        {
            var view = new ChatMessageViewModel(message, SelectedCharacter?.AvatarPath);
            var date = DateOnly.FromDateTime(message.CreatedAt.LocalDateTime.Date);
            view.ShowDateSeparator = previousDate != date;
            view.DateSeparatorLabel = message.CreatedAt.LocalDateTime.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
            previousDate = date;
            Messages.Add(view);
        }
        OnPropertyChanged(nameof(SelectedChatMessageCount));
        OnPropertyChanged(nameof(SelectedChatLastMessageLabel));
        RefreshChatMessageSearchResults();
    }

    private void RefreshStateVariableValues()
    {
        StateVariableValues.Clear();
        if (SelectedCharacter is null) return;
        foreach (var variable in SelectedCharacter.StateVariables.OrderBy(x => x.DisplayOrder))
        {
            var value = SelectedChat is not null && SelectedChat.StateValuesJson.TryGetValue(variable.Id, out var chatValue)
                ? chatValue
                : variable.DefaultValueJson;
            StateVariableValues.Add(new StateVariableContextItem(variable.DisplayName, variable.Key, value, variable.VariableType));
        }
    }

    private void RefreshLorebookBindingFlag()
    {
        var bound = SelectedLorebook is not null && SelectedCharacter?.LorebookIds.Contains(SelectedLorebook.Id) == true;
        if (_isSelectedLorebookBound == bound) return;
        _isSelectedLorebookBound = bound;
        OnPropertyChanged(nameof(IsSelectedLorebookBound));
    }

    private async Task ReloadLorebooksAsync(Guid? selectId = null)
    {
        var books = await _lorebooks.GetLorebooksAsync();
        Lorebooks.Clear();
        foreach (var book in books) Lorebooks.Add(book);
        // Присваивание через сеттер обязательно обновляет доступность команд сохранения, добавления и удаления записей.
        SelectedLorebook = selectId is null ? Lorebooks.FirstOrDefault() : Lorebooks.FirstOrDefault(x => x.Id == selectId);
        OnPropertyChanged(nameof(IsSelectedLorebookBound));
    }

    private async Task ReloadPersonasAsync(Guid? selectId = null)
    {
        var currentId = selectId ?? SelectedPersona?.Id;
        var personas = await _personas.GetPersonasAsync();
        Personas.Clear();
        foreach (var persona in personas) Personas.Add(persona);
        SelectedPersona = currentId is null ? Personas.FirstOrDefault() : Personas.FirstOrDefault(persona => persona.Id == currentId);
        OnPropertyChanged(nameof(HasPersonas));
        OnPropertyChanged(nameof(SelectedCharacterPersonaId));
        OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
    }

    private async Task ReloadScenesAsync(Guid? selectId = null)
    {
        Guid? currentId = selectId;
        if (currentId is null)
            await UpdateSceneUiAsync(() => currentId = _selectedScene?.Id);

        var scenes = await _scenes.GetScenesAsync().ConfigureAwait(false);
        Guid? targetId = null;
        await UpdateSceneUiAsync(() =>
        {
            Scenes.Clear();
            foreach (var scene in scenes) Scenes.Add(scene);
            _selectedScene = currentId is null ? Scenes.FirstOrDefault() : Scenes.FirstOrDefault(scene => scene.Id == currentId);
            targetId = _selectedScene?.Id;
            OnPropertyChanged(nameof(SelectedScene));
            OnPropertyChanged(nameof(IsSceneSelected));
            OnPropertyChanged(nameof(IsSceneConversationVisible));
            OnPropertyChanged(nameof(SelectedSceneCharacterA));
            OnPropertyChanged(nameof(SelectedSceneCharacterB));
            OnPropertyChanged(nameof(SceneParticipants));
            OnPropertyChanged(nameof(SceneParticipantNames));
            OnPropertyChanged(nameof(SceneNextSpeakerName));
            OnPropertyChanged(nameof(SceneLastMessageLabel));
            RefreshSceneMessageSearchResults();
            OnPropertyChanged(nameof(SceneStartPauseText));
            OnPropertyChanged(nameof(IsSceneFinished));
            RaiseSceneCommands();
        });
        await LoadSelectedSceneAsync(targetId);
        RebuildConversationItems();
    }

    private async Task LoadSelectedSceneAsync(Guid? sceneId)
    {
        var fresh = sceneId is null ? null : await _scenes.GetSceneAsync(sceneId.Value).ConfigureAwait(false);
        await UpdateSceneUiAsync(() =>
        {
            SceneMessages.Clear();
            if (fresh is null)
            {
                _selectedScene = null;
                OnPropertyChanged(nameof(SelectedScene));
                OnPropertyChanged(nameof(IsSceneSelected));
                OnPropertyChanged(nameof(IsSceneConversationVisible));
                OnPropertyChanged(nameof(SelectedSceneCharacterA));
                OnPropertyChanged(nameof(SelectedSceneCharacterB));
                OnPropertyChanged(nameof(SceneParticipants));
                OnPropertyChanged(nameof(SceneParticipantNames));
                OnPropertyChanged(nameof(SceneNextSpeakerName));
                OnPropertyChanged(nameof(SceneStartPauseText));
                OnPropertyChanged(nameof(IsSceneFinished));
                RaiseSceneCommands();
                return;
            }

            var index = Scenes.ToList().FindIndex(scene => scene.Id == fresh.Id);
            if (index >= 0) Scenes[index] = fresh;
            _selectedScene = fresh;
            foreach (var message in fresh.Messages.OrderBy(message => message.SequenceNumber))
            {
                var avatarPath = message.SpeakerCharacterId is Guid speakerId
                    ? Characters.FirstOrDefault(character => character.Id == speakerId)?.AvatarPath
                    : null;
                SceneMessages.Add(new SceneMessageViewModel(message, fresh.CharacterAId, avatarPath));
            }
            OnPropertyChanged(nameof(SelectedScene));
            OnPropertyChanged(nameof(IsSceneSelected));
            OnPropertyChanged(nameof(SelectedSceneCharacterA));
            OnPropertyChanged(nameof(SelectedSceneCharacterB));
            OnPropertyChanged(nameof(SceneParticipants));
            OnPropertyChanged(nameof(SceneParticipantNames));
            OnPropertyChanged(nameof(SceneNextSpeakerName));
            OnPropertyChanged(nameof(SceneLastMessageLabel));
            RefreshSceneMessageSearchResults();
            OnPropertyChanged(nameof(SceneStartPauseText));
            OnPropertyChanged(nameof(IsSceneFinished));
            RaiseSceneCommands();
        });
    }

    private void BeginCreateScene()
    {
        IsSceneComposerOpen = true;
        SceneRunStatus = "Заполните участников и условия новой сцены.";
    }

    private async Task CreateSceneAsync()
    {
        if (SceneCharacterA is null || SceneCharacterB is null || SceneCharacterA.Id == SceneCharacterB.Id) return;
        try
        {
            IsBusy = true;
            var scene = await _scenes.CreateAsync(SceneCharacterA.Id, SceneCharacterB.Id, SceneNameDraft, SceneScenarioDraft, SceneLocationDraft, SceneTimeDraft, SceneMoodDraft, SceneGoalDraft, SceneCharacterA.Id, SceneTurnModeDraft, SceneDelaySecondsDraft, SceneEnforceContractDraft, SceneRelationshipDraft, SceneAdvanceNarrativeDraft);
            SceneNameDraft = "Очередь за попкорном"; SceneScenarioDraft = "Два персонажа стоят в очереди за попкорном перед вечерним сеансом и начинают непринуждённый разговор."; SceneLocationDraft = "Фойе кинотеатра"; SceneTimeDraft = "Вечер перед началом фильма"; SceneMoodDraft = "Лёгкое любопытство"; SceneGoalDraft = "Познакомиться и обсудить фильм"; SceneRelationshipDraft = ""; SceneAdvanceNarrativeDraft = true;
            IsSceneComposerOpen = false;
            await ReloadScenesAsync(scene.Id);
            var conversation = ConversationItems.FirstOrDefault(item => item.IsScene && item.Id == scene.Id);
            if (conversation is not null) SelectedConversationItem = conversation;
            SceneRunStatus = "Сцена создана. Нажмите «Старт» или сделайте следующий ход вручную.";
        }
        catch (Exception ex) { HandleError("Не удалось создать сцену", ex); }
        finally { IsBusy = false; }
    }

    private async Task SaveSceneAsync()
    {
        if (SelectedScene is null) return;
        try
        {
            await _scenes.UpdateAsync(SelectedScene);
            await ReloadScenesAsync(SelectedScene.Id);
            SceneRunStatus = "Параметры сцены сохранены.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить сцену", ex); }
    }

    private async Task DeleteSceneAsync()
    {
        if (SelectedScene is null) return;
        try
        {
            CancelSceneTimer();
            await _scenes.DeleteAsync(SelectedScene.Id);
            await ReloadScenesAsync();
            SceneRunStatus = "Сцена удалена.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить сцену", ex); }
    }

    private async Task ToggleSceneStartPauseAsync()
    {
        if (SelectedScene is null || SelectedScene.Status == "finished") return;
        if (SelectedScene.Status == "running") await PauseSceneAsync();
        else await StartSceneAsync();
    }

    private async Task StartSceneAsync()
    {
        if (SelectedScene is null) return;
        await _scenes.SetStatusAsync(SelectedScene.Id, "running");
        await LoadSelectedSceneAsync(SelectedScene.Id);
        SceneRunStatus = $"Сцена запущена. Следующий ход: {SceneNextSpeakerName}.";
        if (SelectedScene.DelaySeconds >= 5 && SelectedScene.TurnMode == "alternate") ScheduleSceneTimer();
    }

    private async Task PauseSceneAsync()
    {
        if (SelectedScene is null) return;
        CancelSceneTimer();
        await _scenes.SetStatusAsync(SelectedScene.Id, "paused");
        await LoadSelectedSceneAsync(SelectedScene.Id);
        ScheduleSceneSummary(SelectedScene.CharacterAId, SelectedScene.Id, immediate: true);
        SceneRunStatus = "Сцена поставлена на паузу. История и контекст сохранены; Summary обновится в фоне при необходимости.";
    }

    private async Task FinishSceneAsync()
    {
        if (SelectedScene is null) return;
        CancelSceneTimer();
        await _scenes.SetStatusAsync(SelectedScene.Id, "finished");
        await LoadSelectedSceneAsync(SelectedScene.Id);
        ScheduleSceneSummary(SelectedScene.CharacterAId, SelectedScene.Id, immediate: true);
        SceneRunStatus = "Сцена завершена. Обычные чаты персонажей не изменялись; Summary обновится в фоне при необходимости.";
    }

    private async Task ChooseSceneSpeakerAsync(SoulCharacter? character)
    {
        if (SelectedScene is null || character is null) return;
        if (character.Id != SelectedScene.CharacterAId && character.Id != SelectedScene.CharacterBId) return;
        CancelSceneTimer();
        await _scenes.SetStatusAsync(SelectedScene.Id, "paused", character.Id);
        await LoadSelectedSceneAsync(SelectedScene.Id);
        SceneRunStatus = $"Следующий ход вручную назначен персонажу {character.Name}.";
    }

    private async Task AddDirectorEventAsync()
    {
        if (SelectedScene is null || string.IsNullOrWhiteSpace(SceneDirectorDraft)) return;
        try
        {
            await _scenes.AddDirectorMessageAsync(SelectedScene.Id, SceneDirectorDraft);
            SceneDirectorDraft = "";
            await LoadSelectedSceneAsync(SelectedScene.Id);
            SceneRunStatus = "Режиссёрское событие добавлено в общий контекст сцены.";
        }
        catch (Exception ex) { HandleError("Не удалось добавить режиссёрское событие", ex); }
    }

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
                await _scenes.SetStatusAsync(sceneId, "running", token: token);
                StartNetworkSceneLoop(sceneId);
                return;
            case "pause":
                StopNetworkSceneLoop(sceneId);
                await _scenes.SetStatusAsync(sceneId, "paused", token: token);
                return;
            case "next":
                await GenerateNetworkSceneTurnAsync(sceneId, token);
                StartNetworkSceneLoop(sceneId);
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
                    var chatId = SelectedChat?.Id;
                    var sceneId = SelectedScene?.Id;
                    await ReloadCharactersAsync(characterId);
                    if (chatId is not null)
                    {
                        var restoredChat = Chats.FirstOrDefault(chat => chat.Id == chatId.Value);
                        if (restoredChat is not null)
                        {
                            _selectedChat = restoredChat;
                            OnPropertyChanged(nameof(SelectedChat));
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

    private async Task GenerateNetworkSceneTurnAsync(Guid sceneId, CancellationToken token)
    {
        try
        {
            var settings = await BuildLlamaSettingsAsync();
            var result = await _conversationTurnRunner.RunSceneTurnAsync(
                sceneId,
                settings.ContextSize,
                settings.MaxTokens,
                (messages, cancellation) => _llama.GenerateFromMessagesAsync(settings, messages, cancellation, "network_scene_" + Guid.NewGuid().ToString("N")[..8]),
                (speaker, raw) => SceneResponseFormatter.NormalizeRoleplayLayout(
                    SceneResponseFormatter.RemoveOwnLeadingLabel(_stateVariables.RemoveStateBlocks(raw), speaker.Name),
                    speaker.UseRoleplayResponseFormatting),
                started => AppLog.Write($"NETWORK_SCENE_BEGIN scene={started.SceneId:N} speaker={started.SpeakerCharacterId:N}"),
                token: token);
            if (result.Status != SceneTurnExecutionStatus.Completed) return;
            var scene = await _scenes.GetSceneAsync(sceneId, token);
            if (scene is not null) ScheduleSceneSummary(scene.CharacterAId, sceneId);
            AppLog.Write($"NETWORK_SCENE_SAVED scene={sceneId:N} message={result.SavedMessage?.Id:N} chars={result.Content.Length} status={result.NextStatus}");
        }
        catch (Exception ex) when (IsContextCapacityError(ex))
        {
            await PauseSceneAfterContextCapacityErrorAsync(sceneId, token);
            AppLog.Write($"NETWORK_SCENE_PAUSED_CONTEXT_LIMIT scene={sceneId:N}: {ex.Message}");
            throw new InvalidOperationException("Контекст сцены достиг лимита модели. Сцена поставлена на паузу; повторите ход после сокращения истории.", ex);
        }
    }

    private void StartNetworkSceneLoop(Guid sceneId)
    {
        StopNetworkSceneLoop(sceneId);
        var source = new CancellationTokenSource();
        _networkSceneLoops[sceneId] = source;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!source.IsCancellationRequested)
                {
                    var scene = await _scenes.GetSceneAsync(sceneId, source.Token);
                    if (scene is null || scene.Status != "running" || scene.TurnMode != "alternate" || scene.DelaySeconds < 5) break;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(scene.DelaySeconds, 5, 30)), source.Token);
                    if (!source.IsCancellationRequested) await GenerateNetworkSceneTurnAsync(sceneId, source.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLog.Write($"NETWORK_SCENE_LOOP_FAILED scene={sceneId:N}: {ex}"); }
            finally
            {
                if (_networkSceneLoops.TryGetValue(sceneId, out var active) && ReferenceEquals(active, source))
                    _networkSceneLoops.TryRemove(sceneId, out _);
                source.Dispose();
            }
        });
    }

    private void StopNetworkSceneLoop(Guid sceneId)
    {
        if (_networkSceneLoops.TryRemove(sceneId, out var source)) source.Cancel();
    }

    private async Task GenerateNextSceneTurnAsync()
    {
        var scene = SelectedScene;
        if (scene is null || IsSceneGenerating || IsBusy) return;
        try
        {
            CancelSceneTimer();
            _cognitiveScheduler.Cancel(scene.CharacterAId, scene.Id);
            IsSceneGenerating = true;
            await _scenes.UpdateAsync(scene);
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
                        SceneMessages.Add(live);
                        liveAdded = true;
                        IsSceneTyping = false;
                    }
                    live?.SetContent(preview);
                }));
            }

            var result = await Task.Run(async () => await _conversationTurnRunner.RunSceneTurnAsync(
                scene.Id,
                settings.ContextSize,
                settings.MaxTokens,
                (messages, cancellation) => _llama.GenerateFromMessagesAsync(settings, messages, cancellation, "scene_" + Guid.NewGuid().ToString("N")[..8]),
                (speaker, raw) => SceneResponseFormatter.NormalizeRoleplayLayout(
                    SceneResponseFormatter.RemoveOwnLeadingLabel(_stateVariables.RemoveStateBlocks(raw), speaker.Name),
                    speaker.UseRoleplayResponseFormatting),
                started =>
                {
                    live = SceneMessageViewModel.Live(started.Speaker.Name, started.SpeakerCharacterId == scene.CharacterAId, started.Speaker.AvatarPath);
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
                SceneRunStatus = "Для этой сцены уже формируется реплика.";
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
                    SceneMessages.Add(live);
                    liveAdded = true;
                }
                live?.SetContent(text);
            });

            var savedMessage = result.SavedMessage ?? throw new InvalidOperationException("Общий runner не вернул сохранённую реплику сцены.");
            var otherId = result.NextSpeakerCharacterId;
            var nextStatus = result.NextStatus;

            // Keep the selected scene in sync without recreating SceneMessages and without replacing the live bubble.
            await UpdateSceneUiAsync(() =>
            {
                if (SelectedScene?.Id == scene.Id)
                {
                    if (!SelectedScene.Messages.Any(message => message.Id == savedMessage.Id))
                        SelectedScene.Messages.Add(savedMessage);
                    SelectedScene.Status = nextStatus;
                    SelectedScene.NextCharacterId = otherId;
                    SelectedScene.UpdatedAt = savedMessage.CreatedAt;
                    OnPropertyChanged(nameof(SelectedScene));
                    OnPropertyChanged(nameof(SceneNextSpeakerName));
                    OnPropertyChanged(nameof(SceneStartPauseText));
                    OnPropertyChanged(nameof(IsSceneFinished));
                    OnPropertyChanged(nameof(SceneLastMessageLabel));
                    RefreshSceneMessageSearchResults();
                    RebuildConversationItems();
                    RaiseSceneCommands();
                }
            });

            ScheduleSceneSummary(scene.CharacterAId, scene.Id);
            SceneRunStatus = $"{result.SpeakerName} ответил. Общий Summary при необходимости обновится в фоне после короткой паузы.";
            if (SelectedScene?.Status == "running" && SelectedScene.TurnMode == "alternate" && SelectedScene.DelaySeconds >= 5) ScheduleSceneTimer();
        }
        catch (Exception ex)
        {
            if (IsContextCapacityError(ex))
            {
                await PauseSceneAfterContextCapacityErrorAsync(scene.Id);
                SceneRunStatus = "Контекст сцены достиг лимита модели. Сцена поставлена на паузу; следующий ход после обновления SoulExe автоматически сократит старую историю.";
            }
            HandleError("Не удалось выполнить ход сцены", ex);
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
            var summary = await _scenes.UpdateSummaryAsync(sceneId, CompleteSceneSummaryAsync, false, 6, token);
            if (!summary.Updated) return;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
            {
                await dispatcher.InvokeAsync(async () =>
                {
                    if (SelectedScene?.Id != sceneId) return;
                    await LoadSelectedSceneAsync(sceneId);
                    SceneRunStatus = "Общий Summary сцены обновлён в фоне.";
                }).Task.Unwrap();
            }
        });
    }

    private async Task<string> CompleteSceneSummaryAsync(IReadOnlyList<LlamaMessage> messages, CancellationToken token)
    {
        var settings = await BuildLlamaSettingsAsync();
        settings.MaxTokens = Math.Clamp(Math.Min(settings.MaxTokens, 220), 128, 220);
        settings.Temperature = Math.Min(settings.Temperature, 0.3);
        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in _llama.GenerateFromMessagesAsync(settings, messages, token, "scene_summary_" + Guid.NewGuid().ToString("N")[..8])) answer.Append(chunk);
        return answer.ToString();
    }

    private async Task UpdateSceneUiAsync(Action update)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) { update(); return; }
        await dispatcher.InvokeAsync(update).Task;
    }

    private void ScheduleSceneTimer()
    {
        if (SelectedScene is null || SelectedScene.DelaySeconds < 5) return;
        CancelSceneTimer();
        var sceneId = SelectedScene.Id;
        var delay = SelectedScene.DelaySeconds;
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
                    isCurrentScene = SelectedScene?.Id == sceneId && SelectedScene?.Status == "running";
                    if (isCurrentScene)
                    {
                        SceneCountdownSeconds = remaining;
                        SceneRunStatus = $"Следующая реплика {SceneNextSpeakerName} через {remaining} сек. Нажмите «Пауза», чтобы остановить таймер.";
                    }
                });
                if (!isCurrentScene) return;
                await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested) return;
            var readyToGenerate = false;
            await UpdateSceneUiAsync(() => readyToGenerate = SelectedScene?.Id == sceneId && SelectedScene?.Status == "running");
            if (!readyToGenerate || token.IsCancellationRequested) return;
            if (Application.Current?.Dispatcher is not null)
                await Application.Current.Dispatcher.InvokeAsync(async () => await GenerateNextSceneTurnAsync()).Task.Unwrap();
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

    private void RaiseSceneCommands()
    {
        CreateSceneCommand?.RaiseCanExecuteChanged(); SaveSceneCommand?.RaiseCanExecuteChanged(); DeleteSceneCommand?.RaiseCanExecuteChanged(); StartSceneCommand?.RaiseCanExecuteChanged(); PauseSceneCommand?.RaiseCanExecuteChanged(); ToggleSceneStartPauseCommand?.RaiseCanExecuteChanged(); NextSceneTurnCommand?.RaiseCanExecuteChanged(); ChooseSceneSpeakerCommand?.RaiseCanExecuteChanged(); AddDirectorEventCommand?.RaiseCanExecuteChanged(); FinishSceneCommand?.RaiseCanExecuteChanged();
    }

    private void OpenLibraryLoreEditor(SoulLorebook? lorebook)
    {
        if (lorebook is null) return;
        LibraryTab = "lore";
        SelectedLorebook = lorebook;
        IsLibraryLoreEditorOpen = true;
    }

    private async Task AddLorebookAsync()
    {
        try
        {
            IsBusy = true;
            var book = await _lorebooks.CreateAsync($"Лорбук {Lorebooks.Count + 1}");
            await ReloadLorebooksAsync(book.Id);
            IsLibraryLoreEditorOpen = true;
            Status = $"Создан лорбук «{book.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать лорбук", ex); }
        finally { IsBusy = false; }
    }

    private async Task SaveLorebookAsync()
    {
        if (SelectedLorebook is null) return;
        try
        {
            await _lorebooks.UpdateAsync(SelectedLorebook);
            Status = "Лорбук сохранён.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить лорбук", ex); }
    }

    private async Task DeleteLorebookAsync(SoulLorebook? book)
    {
        if (book is null) return;
        await _lorebooks.DeleteAsync(book.Id);
        if (SelectedLorebook?.Id == book.Id) { IsLibraryLoreEditorOpen = false; SelectedLorebook = null; }
        await ReloadLorebooksAsync();
        Status = $"Лорбук «{book.Name}» удалён.";
    }

    private async Task DeleteLoreEntryAsync(SoulLoreEntry? entry)
    {
        if (SelectedLorebook is null || entry is null) return;
        try
        {
            IsBusy = true;
            await _lorebooks.DeleteEntryAsync(SelectedLorebook.Id, entry.Id);
            await ReloadLorebooksAsync(SelectedLorebook.Id);
            Status = "Запись лорбука удалена.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить запись лорбука", ex); }
        finally { IsBusy = false; }
    }

    private async Task AddLoreEntryAsync()
    {
        if (SelectedLorebook is null) return;
        try
        {
            await _lorebooks.AddEntryAsync(SelectedLorebook.Id);
            await ReloadLorebooksAsync(SelectedLorebook.Id);
            Status = "Добавлена запись лорбука.";
        }
        catch (Exception ex) { HandleError("Не удалось добавить запись лорбука", ex); }
    }

    private async Task SetLorebookBindingAsync(bool bind)
    {
        if (SelectedCharacter is null || SelectedLorebook is null) return;
        try
        {
            await _lorebooks.BindAsync(SelectedCharacter.Id, SelectedLorebook.Id, bind);
            if (bind && !SelectedCharacter.LorebookIds.Contains(SelectedLorebook.Id)) SelectedCharacter.LorebookIds.Add(SelectedLorebook.Id);
            if (!bind) SelectedCharacter.LorebookIds.RemoveAll(x => x == SelectedLorebook.Id);
            Status = bind ? "Лорбук привязан к персонажу." : "Лорбук отключён для персонажа.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить привязку лорбука", ex); }
    }

    private void OpenPersonaEditor(SoulPersona? persona)
    {
        if (persona is null) return;
        LibraryTab = "personas";
        SelectedPersona = persona;
        IsPersonaEditorOpen = true;
    }

    private async Task AddPersonaAsync()
    {
        try
        {
            IsBusy = true;
            var persona = await _personas.CreateAsync($"Персона {Personas.Count + 1}");
            await ReloadPersonasAsync(persona.Id);
            LibraryTab = "personas";
            IsPersonaEditorOpen = true;
            Status = $"Создана персона «{persona.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать персону", ex); }
        finally { IsBusy = false; }
    }

    private async Task SavePersonaAsync()
    {
        if (SelectedPersona is null) return;
        try
        {
            IsBusy = true;
            var personaId = SelectedPersona.Id;
            await _personas.UpdateAsync(SelectedPersona);
            await ReloadPersonasAsync(personaId);
            Status = "Персона сохранена.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить персону", ex); }
        finally { IsBusy = false; }
    }

    private void ConfirmDeletePersona(SoulPersona? persona)
    {
        if (persona is not null) PersonaPendingDeletion = persona;
    }

    private async Task DeletePersonaAsync()
    {
        var persona = PersonaPendingDeletion;
        if (persona is null) return;
        try
        {
            IsBusy = true;
            var selectedCharacterId = SelectedCharacter?.Id;
            PersonaPendingDeletion = null;
            await _personas.DeleteAsync(persona.Id);
            if (SelectedPersona?.Id == persona.Id)
            {
                IsPersonaEditorOpen = false;
                SelectedPersona = null;
            }
            await ReloadPersonasAsync();
            await ReloadCharactersAsync(selectedCharacterId);
            Status = $"Персона «{persona.Name}» удалена и отключена у связанных персонажей.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить персону", ex); }
        finally { IsBusy = false; }
    }

    private Task AddCharacterAsync()
    {
        OpenCharacterCreationDialog();
        return Task.CompletedTask;
    }

    private void OpenCharacterCreationDialog()
    {
        CharacterCreationMode = string.Empty;
        CharacterNameDraft = string.Empty;
        CharacterGenerationIdea = string.Empty;
        IsCharacterCreationDialogOpen = true;
    }

    private void SelectCharacterCreationMode(string? mode)
    {
        CharacterCreationMode = mode is "manual" or "generate" ? mode : string.Empty;
    }

    private void CloseCharacterCreationDialog()
    {
        IsCharacterCreationDialogOpen = false;
        CharacterCreationMode = string.Empty;
        CharacterNameDraft = string.Empty;
        CharacterGenerationIdea = string.Empty;
    }

    private async Task CreateCharacterWithNameAsync()
    {
        var name = CharacterNameDraft.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            IsBusy = true;
            var character = await _library.CreateCharacterAsync(name);
            await ReloadCharactersAsync(character.Id);
            CharacterEditorTab = "info";
            CloseCharacterCreationDialog();
            CurrentPage = "Characters";
            Status = $"Создан персонаж «{character.Name}». Заполните карточку и сохраните изменения.";
        }
        catch (Exception ex) { HandleError("Не удалось создать персонажа", ex); }
        finally { IsBusy = false; }
    }

    private async Task LoadGatewayAsync(bool append = false)
    {
        try
        {
            IsBusy = true;
            if (!append)
            {
                _gatewayPage = 1;
                GatewayItems.Clear();
                GatewayHasMore = GatewayCategory == "chub";
            }
            else _gatewayPage++;

            var results = await _charactersGateway.GetAssetsAsync(GatewayCategory, GatewayQuery, GatewayNsfwEnabled, _gatewayPage);
            var installedLoreNames = Lorebooks
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var known = GatewayItems.Select(x => $"{x.Kind}:{x.Id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var result in results.Where(x => known.Add($"{x.Kind}:{x.Id}")))
            {
                if (result.Kind == "lorebook")
                {
                    result.IsAlreadyImported = installedLoreNames.Contains(result.Name);
                }
                GatewayItems.Add(result);
                added++;
            }
            GatewayHasMore = GatewayCategory == "chub" && results.Count >= 30 && added > 0;
            SelectedGatewayAsset = GatewayItems.FirstOrDefault();
            Status = GatewayItems.Count == 0
                ? $"В категории «{GatewayCategoryTitle}» ничего не найдено."
                : $"{GatewayCategoryTitle}: загружено {GatewayItems.Count} материалов{(GatewayHasMore ? ". Можно загрузить ещё." : ".")}";
        }
        catch (Exception ex)
        {
            if (append) _gatewayPage = Math.Max(1, _gatewayPage - 1);
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
            await _store.MutateAsync(root =>
            {
                root.Preferences.CognitiveSoulMemoryEnabled = memoryEnabled;
                root.Preferences.SoulMemoryPreset = memoryPreset;
                root.Preferences.CognitiveMemoryIntervalMessages = memoryInterval;
                root.Preferences.CognitiveAutoSummaryEnabled = summaryEnabled;
                root.Preferences.CognitiveSummaryIntervalMessages = summaryInterval;
                root.Preferences.CognitiveBackgroundMode = backgroundMode;
                root.Preferences.CognitiveBackgroundIdleSeconds = backgroundIdleSeconds;
            }, "save_cognitive_architecture");
        }
        catch (Exception ex) { AppLog.Write("Не удалось сохранить настройки Cognitive Architecture.", ex); }
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

    private async Task ExportCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        var suggestedName = string.Concat(SelectedCharacter.Name.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
        var dialog = new SaveFileDialog { Filter = "Character Card JSON|*.json|Character Card PNG (нужен PNG-аватар)|*.png", FileName = string.IsNullOrWhiteSpace(suggestedName) ? "character" : suggestedName, AddExtension = true };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            await _characterCardExporter.ExportAsync(SelectedCharacter, dialog.FileName);
            Status = $"Карточка экспортирована: {dialog.FileName}";
        }
        catch (Exception ex) { HandleError("Не удалось экспортировать карточку", ex); }
        finally { IsBusy = false; }
    }

    private async Task ImportSoulOfWaifuAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку старой установки Soul-of-Waifu" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var report = await _soulOfWaifuImporter.ImportAsync(dialog.FolderName);
            await ReloadCharactersAsync();
            Status = report.ToDisplayText();
        }
        catch (Exception ex) { HandleError("Не удалось перенести данные Soul-of-Waifu", ex); }
        finally { IsBusy = false; }
    }

    private async Task ImportCharacterAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Character Card V2|*.png;*.json|PNG image|*.png|JSON card|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var character = await _characterCards.ImportAsync(dialog.FileName);
            await ReloadCharactersAsync(character.Id);
            Status = $"Импортирован персонаж «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось импортировать карточку", ex); }
        finally { IsBusy = false; }
    }

    private async Task OpenCharacterChatAsync(SoulCharacter? character)
    {
        if (character is null) return;
        try
        {
            IsBusy = true;
            var chat = await _library.CreateChatAsync(character.Id, "Новый чат");
            // Сбрасываем старый элемент общего списка: иначе он имеет приоритет и визуально остаётся открытым.
            _selectedConversationItem = null;
            OnPropertyChanged(nameof(SelectedConversationItem));
            OnPropertyChanged(nameof(IsSceneChatActive));
            await ReloadCharactersAsync(character.Id);
            _selectedChat = Chats.FirstOrDefault(item => item.Id == chat.Id);
            OnPropertyChanged(nameof(SelectedChat));
            LoadMessages();
            RebuildConversationItems();
            CurrentPage = "Chat";
            Status = $"Создан и открыт чат «{chat.Name}» с персонажем «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать чат для персонажа", ex); }
        finally { IsBusy = false; }
    }

    private async Task OpenCharacterEditorAsync(SoulCharacter? character)
    {
        if (character is null) return;
        try
        {
            IsBusy = true;
            await ReloadCharactersAsync(character.Id);
            CharacterEditorTab = "info";
            CurrentPage = "Characters";
            Status = $"Открыта карточка персонажа «{character.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось открыть редактор персонажа", ex); }
        finally { IsBusy = false; }
    }

    private Task ConfirmDeleteCharacterAsync(SoulCharacter? character)
    {
        if (character is null || Characters.Count <= 1) return Task.CompletedTask;
        CharacterPendingDeletion = character;
        return Task.CompletedTask;
    }

    private async Task ConfirmCharacterDeleteAsync()
    {
        var character = CharacterPendingDeletion;
        if (character is null) return;
        CharacterPendingDeletion = null;
        SelectedCharacter = character;
        await DeleteCharacterAsync();
    }

    private async Task DeleteCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        try
        {
            IsBusy = true;
            await _library.DeleteCharacterAsync(SelectedCharacter.Id);
            await ReloadCharactersAsync();
            Status = "Персонаж удалён.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить персонажа", ex); }
        finally { IsBusy = false; }
    }

    private async Task OpenChatListItemAsync(ChatListItemViewModel item)
    {
        try
        {
            await _library.SelectChatAsync(item.CharacterId, item.ChatId);
            await ReloadCharactersAsync(item.CharacterId);
            CurrentPage = "Chat";
        }
        catch (Exception ex) { HandleError("Не удалось открыть выбранный чат", ex); }
    }

    private void OpenChatActionMenu(ChatListItemViewModel? item)
    {
        if (item is null) return;
        if (ChatActionMenuItem is not null && ChatActionMenuItem != item) ChatActionMenuItem.IsActionMenuOpen = false;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        ChatActionMenuItem = item;
        IsMessageActionMenuOpen = false;
        IsChatActionMenuOpen = true;
        item.IsActionMenuOpen = true;
    }

    private void CloseChatActionMenu()
    {
        if (ChatActionMenuItem is not null) ChatActionMenuItem.IsActionMenuOpen = false;
        IsChatActionMenuOpen = false;
        ChatActionMenuItem = null;
    }

    private void OpenMessageActionMenu(ChatMessageViewModel? item)
    {
        if (item is null) return;
        if (MessageActionMenuItem is not null && MessageActionMenuItem != item) MessageActionMenuItem.IsActionMenuOpen = false;
        if (ChatActionMenuItem is not null) ChatActionMenuItem.IsActionMenuOpen = false;
        MessageActionMenuItem = item;
        IsChatActionMenuOpen = false;
        IsMessageActionMenuOpen = true;
        item.IsActionMenuOpen = true;
    }

    private Task OpenNewChatCharacterPickerAsync()
    {
        NewConversationType = "chat";
        NewChatCharacter = SelectedCharacter ?? Characters.FirstOrDefault();
        EnsureSceneDraftParticipants();
        NewChatNameDraft = "Новый чат";
        IsNewChatCharacterPickerOpen = true;
        return Task.CompletedTask;
    }

    private async Task CreateNewConversationAsync()
    {
        if (IsNewSceneType)
        {
            if (SceneCharacterA is null || SceneCharacterB is null || SceneCharacterA.Id == SceneCharacterB.Id) return;
            IsNewChatCharacterPickerOpen = false;
            await CreateSceneAsync();
            NewConversationType = "chat";
            return;
        }
        await CreateChatForNewChatCharacterAsync();
    }

    private async Task CreateChatForNewChatCharacterAsync()
    {
        var character = NewChatCharacter;
        if (character is null) return;
        var name = string.IsNullOrWhiteSpace(NewChatNameDraft) ? "Новый чат" : NewChatNameDraft.Trim();
        IsNewChatCharacterPickerOpen = false;
        await CreateChatForCharacterIdAsync(character.Id, character.Name, name);
        NewChatNameDraft = "Новый чат";
    }

    private Task CreateChatForCharacterAsync(ChatListItemViewModel? item) =>
        item is null ? Task.CompletedTask : CreateChatForCharacterIdAsync(item.CharacterId, item.CharacterName, "Новый чат");

    private async Task CreateChatForCharacterIdAsync(Guid characterId, string characterName, string chatName)
    {
        try
        {
            IsBusy = true;
            var chat = await _library.CreateChatAsync(characterId, chatName);
            await ReloadCharactersAsync(characterId);
            _selectedChat = Chats.FirstOrDefault(x => x.Id == chat.Id);
            OnPropertyChanged(nameof(SelectedChat));
            RebuildChatCharacters();
            Status = $"Создан чат «{chat.Name}» для персонажа «{characterName}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать чат", ex); }
        finally { IsBusy = false; }
    }

    private async Task ToggleConversationPinnedAsync(ConversationListItemViewModel? item)
    {
        if (item is null) return;
        if (!item.IsScene)
        {
            await ToggleChatPinnedAsync(item.ChatItem);
            return;
        }
        var scene = Scenes.FirstOrDefault(value => value.Id == item.Id);
        if (scene is null) return;
        try
        {
            IsBusy = true;
            var pinned = !scene.IsPinned;
            await _scenes.SetPinnedAsync(scene.Id, pinned);
            await ReloadScenesAsync(scene.Id);
            Status = pinned ? $"Сцена «{scene.Name}» закреплена вверху списка." : $"Сцена «{scene.Name}» откреплена.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить закрепление сцены", ex); }
        finally { IsBusy = false; }
    }

    private void BeginRenameConversation(ConversationListItemViewModel? item)
    {
        if (item is null) return;
        if (!item.IsScene)
        {
            BeginRenameChat(item.ChatItem);
            return;
        }
        var scene = Scenes.FirstOrDefault(value => value.Id == item.Id);
        if (scene is null) return;
        RenameScene = scene;
        RenameSceneNameDraft = scene.Name;
        IsRenameSceneDialogOpen = true;
    }

    private void CloseRenameSceneDialog()
    {
        IsRenameSceneDialogOpen = false;
        RenameScene = null;
        RenameSceneNameDraft = "";
    }

    private async Task ConfirmRenameSceneAsync()
    {
        var scene = RenameScene;
        var name = RenameSceneNameDraft.Trim();
        if (scene is null || string.IsNullOrWhiteSpace(name)) return;
        try
        {
            IsBusy = true;
            scene.Name = name;
            await _scenes.UpdateAsync(scene);
            CloseRenameSceneDialog();
            await ReloadScenesAsync(scene.Id);
            Status = "Название сцены сохранено.";
        }
        catch (Exception ex) { HandleError("Не удалось переименовать сцену", ex); }
        finally { IsBusy = false; }
    }

    private async Task DeleteConversationAsync(ConversationListItemViewModel? item)
    {
        if (item is null) return;
        if (!item.IsScene)
        {
            await DeleteChatListItemAsync(item.ChatItem);
            return;
        }
        var scene = Scenes.FirstOrDefault(value => value.Id == item.Id);
        if (scene is null) return;
        try
        {
            IsBusy = true;
            CancelSceneTimer();
            await _scenes.DeleteAsync(scene.Id);
            await ReloadScenesAsync();
            Status = $"Сцена «{scene.Name}» удалена.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить сцену", ex); }
        finally { IsBusy = false; }
    }

    private async Task ToggleChatPinnedAsync(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        IsChatActionMenuOpen = false;
        try
        {
            IsBusy = true;
            var pinned = !item.IsPinned;
            await _library.SetChatPinnedAsync(item.CharacterId, item.ChatId, pinned);
            await ReloadCharactersAsync(item.CharacterId);
            Status = pinned
                ? $"Чат «{item.ChatName}» закреплён вверху списка."
                : $"Чат «{item.ChatName}» откреплён.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить закрепление чата", ex); }
        finally { IsBusy = false; }
    }

    private async Task DeleteChatListItemAsync(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        IsChatActionMenuOpen = false;
        try
        {
            IsBusy = true;
            await _library.DeleteChatAsync(item.CharacterId, item.ChatId);
            await ReloadCharactersAsync(item.CharacterId);
            Status = $"Удалён чат «{item.ChatName}».";
        }
        catch (Exception ex) { HandleError("Не удалось удалить чат", ex); }
        finally { IsBusy = false; }
    }

    private void BeginRenameChat(ChatListItemViewModel? item)
    {
        item ??= ChatActionMenuItem;
        if (item is null) return;
        CloseChatActionMenu();
        RenameChatItem = item;
        RenameChatNameDraft = item.ChatName;
        IsRenameChatDialogOpen = true;
    }

    private void CloseRenameChatDialog()
    {
        IsRenameChatDialogOpen = false;
        RenameChatItem = null;
        RenameChatNameDraft = "";
    }

    private async Task ConfirmRenameChatAsync()
    {
        var item = RenameChatItem;
        var name = RenameChatNameDraft.Trim();
        if (item is null || string.IsNullOrWhiteSpace(name)) return;
        try
        {
            IsBusy = true;
            await _library.RenameChatAsync(item.CharacterId, item.ChatId, name);
            CloseRenameChatDialog();
            await ReloadCharactersAsync(item.CharacterId);
            Status = "Название чата сохранено.";
        }
        catch (Exception ex) { HandleError("Не удалось переименовать чат", ex); }
        finally { IsBusy = false; }
    }

    private void CancelRenameChat(ChatListItemViewModel? item)
    {
        if (item is null) return;
        item.ChatNameDraft = item.ChatName;
        item.IsRenaming = false;
        SaveRenameChatCommand.RaiseCanExecuteChanged();
        CancelRenameChatCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveRenameChatAsync(ChatListItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            IsBusy = true;
            await _library.RenameChatAsync(item.CharacterId, item.ChatId, item.ChatNameDraft);
            item.IsRenaming = false;
            SaveRenameChatCommand.RaiseCanExecuteChanged();
            CancelRenameChatCommand.RaiseCanExecuteChanged();
            await ReloadCharactersAsync(item.CharacterId);
            Status = "Название чата сохранено.";
        }
        catch (Exception ex) { HandleError("Не удалось переименовать чат", ex); }
        finally { IsBusy = false; }
    }

    private async Task SaveChatStartingContextAsync()
    {
        var character = SelectedCharacter;
        var chat = SelectedChat;
        if (character is null || chat is null) return;
        try
        {
            IsBusy = true;
            await _library.UpdateChatStartingContextAsync(character.Id, chat.Id, chat.InitialUserProfile, chat.InitialRelationshipContext);
            await ReloadCharactersAsync(character.Id);
            Status = "Стартовый профиль и отношения сохранены для выбранного чата.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить стартовый контекст чата", ex); }
        finally { IsBusy = false; }
    }

    private static bool HasCardTextOverflow(string? text)
    {
        var prepared = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return prepared.Length > 50;
    }

    private static string CharacterCardText(string? text, bool expanded)
    {
        var prepared = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return !expanded && prepared.Length > 50 ? prepared[..50].TrimEnd() + "…" : prepared;
    }

    private void ToggleCharacterCardSection(string? section)
    {
        switch ((section ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "description": _isCharacterDescriptionExpanded = !_isCharacterDescriptionExpanded; break;
            case "personality": _isCharacterPersonalityExpanded = !_isCharacterPersonalityExpanded; break;
            case "scenario": _isCharacterScenarioExpanded = !_isCharacterScenarioExpanded; break;
            default: return;
        }
        RaiseChatPresentationProperties();
    }

    private void RefreshSceneMessageSearchResults()
    {
        SceneMessageSearchResults.Clear();
        foreach (var message in SceneMessages) message.SetSearchHighlighted(false);
        var query = SceneMessageSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query) || SelectedScene is null) return;
        foreach (var message in SelectedScene.Messages.OrderBy(item => item.SequenceNumber))
        {
            var content = message.Content ?? string.Empty;
            if (!content.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            SceneMessageSearchResults.Add(new ChatMessageSearchResult(message.Id, message.SpeakerName, content, message.CreatedAt));
        }
    }

    private void SelectSceneMessageSearchResult(ChatMessageSearchResult? result)
    {
        if (result is null) return;
        SelectedSceneMessageSearchResult = result;
        foreach (var message in SceneMessages) message.SetSearchHighlighted(message.Id == result.MessageId);
        Status = $"Найдена реплика от {result.AuthorName} · {result.CreatedAt.LocalDateTime:dd.MM.yyyy HH:mm}.";
    }

    private void RefreshChatMessageSearchResults()
    {
        ChatMessageSearchResults.Clear();
        foreach (var message in Messages) message.SetSearchHighlighted(false);
        var query = ChatMessageSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query) || SelectedChat is null) return;

        foreach (var message in SelectedChat.Messages.OrderBy(item => item.SequenceNumber))
        {
            var content = message.Variants.FirstOrDefault(item => item.Id == message.CurrentVariantId)?.Content
                ?? message.Variants.FirstOrDefault()?.Content
                ?? string.Empty;
            if (!content.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;
            ChatMessageSearchResults.Add(new ChatMessageSearchResult(message.Id, message.AuthorName, content, message.CreatedAt));
        }
    }

    private void SelectChatMessageSearchResult(ChatMessageSearchResult? result)
    {
        if (result is null) return;
        SelectedChatMessageSearchResult = result;
        foreach (var message in Messages) message.SetSearchHighlighted(message.MessageId == result.MessageId);
        Status = $"Найдена реплика от {result.AuthorName} · {result.CreatedAt.LocalDateTime:dd.MM.yyyy HH:mm}.";
    }

    private async Task DeleteChatAsync()
    {
        if (SelectedCharacter is null || SelectedChat is null) return;
        try
        {
            IsBusy = true;
            await _library.DeleteChatAsync(SelectedCharacter.Id, SelectedChat.Id);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = "Чат удалён.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить чат", ex); }
        finally { IsBusy = false; }
    }

    private async Task ExpandCharacterFieldAsync(string? field)
    {
        if (SelectedCharacter is null) return;
        var normalized = (field ?? string.Empty).Trim().ToLowerInvariant();
        var (fieldName, source) = normalized switch
        {
            "description" => ("описание персонажа", SelectedCharacter.Description),
            "personality" => ("личность персонажа", SelectedCharacter.Personality),
            "scenario" => ("сценарий", SelectedCharacter.Scenario),
            _ => (string.Empty, string.Empty)
        };
        if (string.IsNullOrWhiteSpace(fieldName)) return;
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
            var prompt = $"""
You are an editor of an AI character card. Extend only the supplied field: {fieldName}.
The field context is mandatory: description needs biographical and visual facts, personality needs traits, motives, habits and speaking manner, scenario needs setting and current situation.
Preserve the language of the supplied text. Return only a concise continuation of 200 to 300 characters; do not repeat the source, do not add a heading, commentary, quotes, meta-text, Markdown, or a character name.
Add concrete, internally consistent details that fit the existing text. Do not invent user actions or dialogue.

Current text:
{source.Trim()}
""";
            var request = new[]
            {
                new LlamaMessage("system", "You write concise additions for character-card fields. Follow the user's requested language and output only the completed text fragment."),
                new LlamaMessage("user", prompt)
            };
            var rawResponse = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in _llama.GenerateFromMessagesAsync(settings, request, CancellationToken.None, $"character_field_{normalized}").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            });
            var addition = NormalizeCharacterFieldAddition(rawResponse, source);
            if (string.IsNullOrWhiteSpace(addition))
            {
                Status = "Локальная модель не вернула подходящее дополнение. Попробуйте ещё раз.";
                return;
            }
            var updated = source.TrimEnd() + (source.EndsWith(".", StringComparison.Ordinal) || source.EndsWith("!", StringComparison.Ordinal) || source.EndsWith("?", StringComparison.Ordinal) ? " " : ". ") + addition;
            switch (normalized)
            {
                case "description": SelectedCharacter.Description = updated; break;
                case "personality": SelectedCharacter.Personality = updated; break;
                case "scenario": SelectedCharacter.Scenario = updated; break;
            }
            OnPropertyChanged(nameof(SelectedCharacter));
            Status = $"Поле «{fieldName}» дополнено локальной моделью. Проверьте текст и сохраните карточку персонажа.";
        }
        catch (Exception ex) { HandleError("Не удалось дополнить поле персонажа локальной моделью", ex); }
        finally { IsBusy = false; }
    }

    private static string NormalizeCharacterFieldAddition(string raw, string source)
    {
        var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (text.StartsWith(source.Trim(), StringComparison.OrdinalIgnoreCase)) text = text[source.Trim().Length..].TrimStart(' ', '.', ',', ':', ';', '-', '—');
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > 300)
        {
            var cutoff = text.LastIndexOf(' ', 300);
            text = (cutoff >= 200 ? text[..cutoff] : text[..300]).TrimEnd();
        }
        return text.Trim(' ', '.', ',', ':', ';', '-', '—', '"', '«', '»');
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
            var russianInput = idea.Any(character => (character >= 'А' && character <= 'я') || character is 'Ё' or 'ё');
            var languageLock = russianInput
                ? "LANGUAGE LOCK — RUSSIAN ONLY: The user's idea is in Russian. Every JSON string value (name, title, description, personality, scenario, systemPrompt, firstMessage) MUST be written in natural Russian. English text is forbidden, except for unavoidable proper names."
                : "LANGUAGE LOCK: Write every JSON string value in the same language as the user's idea.";
            var prompt = $"""
Create a complete roleplay character card from the user's idea.
{languageLock}
Return STRICT JSON only, without Markdown or explanation, with these string properties: name, title, description, personality, scenario, systemPrompt, firstMessage.
Rules: description, personality and scenario must each be 200 to 300 characters, concrete and mutually consistent. Name is only the character's name. Title is a short role or status. systemPrompt must contain only one or two neutral meta-instructions about staying in character and not writing for the user: NEVER repeat the name, age, city, biography, interests, or individual traits there. Those facts belong only in the dedicated fields. firstMessage starts a scene but does not speak or act for the user. Do not include <think> tags.

User's character idea:
{idea}
""";
            var request = new[]
            {
                new LlamaMessage("system", $"You generate valid JSON character cards. Return JSON only. {languageLock}"),
                new LlamaMessage("user", prompt)
            };
            var raw = await Task.Run(async () =>
            {
                var response = new StringBuilder();
                await foreach (var chunk in _llama.GenerateFromMessagesAsync(settings, request, CancellationToken.None, "character_card_generator").ConfigureAwait(false)) response.Append(chunk);
                return response.ToString();
            });
            var generated = ParseGeneratedCharacter(raw);
            if (generated is null || string.IsNullOrWhiteSpace(generated.Name))
            {
                Status = "Модель вернула карточку в непонятном формате. Попробуйте уточнить идею и повторить генерацию.";
                return;
            }
            var character = await _library.CreateCharacterAsync(generated.Name);
            character.Title = generated.Title;
            character.Description = LimitGeneratedField(generated.Description, 300);
            character.Personality = LimitGeneratedField(generated.Personality, 300);
            character.Scenario = LimitGeneratedField(generated.Scenario, 300);
            character.SystemPrompt = "Оставайся в образе персонажа и следуй полям его карточки. Не пиши за пользователя и не повторяй биографию или черты характера без повода.";
            character.Greetings = string.IsNullOrWhiteSpace(generated.FirstMessage) ? [] : [new SoulGreeting { Text = generated.FirstMessage, IsPrimary = true, Position = 0 }];
            character.UseRoleplayResponseFormatting = true;
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

    private static GeneratedCharacterCard? ParseGeneratedCharacter(string raw)
    {
        var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        text = text.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("```", string.Empty, StringComparison.Ordinal).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            static string Value(JsonElement element, string name) => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() ?? string.Empty : string.Empty;
            return new GeneratedCharacterCard(Value(root, "name"), Value(root, "title"), Value(root, "description"), Value(root, "personality"), Value(root, "scenario"), Value(root, "systemPrompt"), Value(root, "firstMessage"));
        }
        catch (JsonException) { return null; }
    }

    private static string LimitGeneratedField(string text, int maxLength)
    {
        text = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= maxLength) return text;
        var cutoff = text.LastIndexOf(' ', maxLength);
        return (cutoff > 0 ? text[..cutoff] : text[..maxLength]).TrimEnd();
    }

    private async Task SaveCharacterAsync()
    {
        if (SelectedCharacter is null) return;
        try
        {
            var characterId = SelectedCharacter.Id;
            await _library.UpdateCharacterAsync(SelectedCharacter);
            await ReloadCharactersAsync(characterId);
            Status = "Карточка персонажа сохранена.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить карточку", ex); }
    }

    private void BeginMessageEdit(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null) return;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        message.BeginEditing();
        SaveEditMessageCommand.RaiseCanExecuteChanged();
        CancelEditMessageCommand.RaiseCanExecuteChanged();
    }

    private void CancelMessageEdit(ChatMessageViewModel? message)
    {
        message?.CancelEditing();
        SaveEditMessageCommand.RaiseCanExecuteChanged();
        CancelEditMessageCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveMessageEditAsync(ChatMessageViewModel? message)
    {
        if (message is null || SelectedCharacter is null || SelectedChat is null) return;
        var content = message.EditingContent.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            Status = "Сообщение не может быть пустым.";
            return;
        }
        try
        {
            IsBusy = true;
            await _library.EditMessageAsync(SelectedCharacter.Id, SelectedChat.Id, message.MessageId, content);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = "Сообщение изменено. Summary и Soul Memory этой ветки будут построены заново по обновлённой истории.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить сообщение", ex); }
        finally { IsBusy = false; }
    }

    private async Task DeleteMessageAsync(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null || SelectedCharacter is null || SelectedChat is null) return;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        try
        {
            IsBusy = true;
            await _library.DeleteMessageAsync(SelectedCharacter.Id, SelectedChat.Id, message.MessageId);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = "Сообщение удалено. Контекст памяти этой ветки будет пересобран по оставшейся истории.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить сообщение", ex); }
        finally { IsBusy = false; }
    }

    private async Task ContinueFromMessageAsync(ChatMessageViewModel? message)
    {
        message ??= MessageActionMenuItem;
        if (message is null || !message.CanContinueFromHere || SelectedCharacter is null || SelectedChat is null) return;
        if (MessageActionMenuItem is not null) MessageActionMenuItem.IsActionMenuOpen = false;
        IsMessageActionMenuOpen = false;
        try
        {
            IsBusy = true;
            var removed = await _library.TruncateChatAfterMessageAsync(SelectedCharacter.Id, SelectedChat.Id, message.MessageId);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = removed == 0
                ? "Это уже последнее сообщение чата. Можно продолжать историю."
                : $"Создана новая ветка: удалено последующих сообщений: {removed}. Введите новое сообщение для продолжения.";
        }
        catch (Exception ex) { HandleError("Не удалось продолжить ветку истории", ex); }
        finally { IsBusy = false; }
    }

    private async Task ShiftVariantAsync(ChatMessageViewModel? message, int direction)
    {
        if (message is null || SelectedCharacter is null || SelectedChat is null) return;
        var target = message.GetAdjacentVariant(direction);
        if (target is null) return;
        try
        {
            await _library.SelectResponseVariantAsync(SelectedCharacter.Id, SelectedChat.Id, message.MessageId, target.Id);
            message.SelectVariant(target.Id);
            PreviousVariantCommand.RaiseCanExecuteChanged();
            NextVariantCommand.RaiseCanExecuteChanged();
            Status = $"Выбран вариант {message.CurrentVariantNumber} из {message.VariantCount}.";
        }
        catch (Exception ex) { HandleError("Не удалось сменить вариант ответа", ex); }
    }

    private async Task ContinueChatAsync()
    {
        // Continuation deliberately does not create a user turn. The model receives the existing
        // dialogue ending with its last reply and produces the next in-character reply directly.
        var character = SelectedCharacter;
        var chat = SelectedChat;
        if (character is null || chat is null || IsBusy) return;
        _cognitiveScheduler.Cancel(character.Id, chat.Id);
        ChatMessageViewModel? liveAssistant = null;
        try
        {
            IsBusy = true;
            chat.Messages ??= [];
            Status = "Модель формирует продолжение…";
            IsAssistantTyping = true;

            var dispatcher = Application.Current?.Dispatcher;
            var liveVariant = new SoulMessageVariant { Label = "Формируется", Content = "", CreatedAt = DateTimeOffset.Now };
            var liveRecord = new SoulMessage
            {
                Role = SoulMessageRole.Assistant,
                AuthorName = character.Name,
                CurrentVariantId = liveVariant.Id,
                Variants = [liveVariant],
                CreatedAt = DateTimeOffset.Now
            };
            liveAssistant = new ChatMessageViewModel(liveRecord, character.AvatarPath);
            var liveAssistantAdded = false;
            var previewActive = 1;

            void PublishPreview(string preview)
            {
                if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    if (Volatile.Read(ref previewActive) == 0) return;
                    if (!liveAssistantAdded)
                    {
                        Messages.Add(liveAssistant);
                        liveAssistantAdded = true;
                        IsAssistantTyping = false;
                    }
                    liveVariant.Content = preview;
                    liveAssistant.RefreshStreamingPreview();
                }));
            }

            var assistantText = await Task.Run(async () =>
            {
                var buffer = new StringBuilder();
                var lastPreviewAt = 0L;
                await foreach (var chunk in GenerateAsync(character, chat, "*continue*", CancellationToken.None, isContinuation: true).ConfigureAwait(false))
                {
                    buffer.Append(chunk);
                    var now = Environment.TickCount64;
                    if (now - lastPreviewAt >= 160)
                    {
                        lastPreviewAt = now;
                        PublishPreview(buffer.ToString());
                    }
                }
                return buffer.ToString();
            });

            Interlocked.Exchange(ref previewActive, 0);
            if (string.IsNullOrWhiteSpace(assistantText)) assistantText = "Модель не вернула текст.";
            await Task.Run(() => _stateVariables.ApplyFromResponseAsync(character.Id, chat.Id, assistantText));
            RefreshStateVariableValues();
            assistantText = _stateVariables.RemoveStateBlocks(assistantText);
            assistantText = SceneResponseFormatter.NormalizeRoleplayLayout(assistantText, character.UseRoleplayResponseFormatting);

            var normalizedReply = AppLog.NormalizeForComparison(assistantText);
            var exactRecentMatch = chat.Messages.Where(message => message.Role == SoulMessageRole.Assistant)
                .Select(message => message.Variants.FirstOrDefault(variant => variant.Id == message.CurrentVariantId)?.Content ?? message.Variants.FirstOrDefault()?.Content ?? string.Empty)
                .Any(previous => string.Equals(AppLog.NormalizeForComparison(previous), normalizedReply, StringComparison.Ordinal));
            AppLog.Write($"CHAT_CONTINUATION_RESPONSE character={character.Id} chat={chat.Id} chars={assistantText.Length} hash={AppLog.Fingerprint(assistantText)} exactRecentMatch={exactRecentMatch} preview=«{AppLog.Preview(assistantText)}»");
            var assistant = await Task.Run(() => _library.AddMessageAsync(character.Id, chat.Id, SoulMessageRole.Assistant, character.Name, assistantText));
            chat.Messages.Add(assistant);
            if (!liveAssistantAdded)
            {
                liveVariant.Content = assistantText;
                Messages.Add(liveAssistant);
                liveAssistantAdded = true;
            }
            liveAssistant.AdoptPersistedMessage(assistant);
            chat.UpdatedAt = assistant.CreatedAt;
            if (IsHomePage) RebuildHomeCards();
            RebuildConversationItems();
            _ = ScheduleCognitiveMaintenanceAfterReplyAsync(character.Id, chat.Id);
            Status = "Готово.";
        }
        catch (Exception ex)
        {
            if (liveAssistant is not null) Messages.Remove(liveAssistant);
            HandleError("Ошибка продолжения диалога", ex);
        }
        finally
        {
            IsAssistantTyping = false;
            IsBusy = false;
        }
    }

    private async Task SendAsync()
    {
        // A new user turn takes priority over any scheduled cognitive maintenance for this dialogue.
        var character = SelectedCharacter;
        var chat = SelectedChat;
        if (character is null || chat is null || string.IsNullOrWhiteSpace(Draft)) return;
        _cognitiveScheduler.Cancel(character.Id, chat.Id);
        var text = Draft.Trim();
        Draft = "";
        ChatMessageViewModel? liveAssistant = null;
        try
        {
            IsBusy = true;
            chat.Messages ??= [];
            var user = await Task.Run(() => _library.AddMessageAsync(character.Id, chat.Id, SoulMessageRole.User, "Вы", text));
            var displayedUser = new ChatMessageViewModel(user, character.AvatarPath);
            Messages.Add(displayedUser);
            chat.Messages.Add(user);
            chat.UpdatedAt = user.CreatedAt;

            Status = "Модель формирует ответ…";
            IsAssistantTyping = true;

            // The SSE reader can receive hundreds of small chunks per answer. Keep parsing and
            // string accumulation off the WPF dispatcher, then refresh the visual preview at most
            // roughly 12 times per second. This prevents generation from starving mouse, scrolling,
            // window movement and repainting on the UI thread.
            var dispatcher = Application.Current?.Dispatcher;
            var liveVariant = new SoulMessageVariant { Label = "Формируется", Content = "", CreatedAt = DateTimeOffset.Now };
            var liveRecord = new SoulMessage
            {
                Role = SoulMessageRole.Assistant,
                AuthorName = character.Name,
                CurrentVariantId = liveVariant.Id,
                Variants = [liveVariant],
                CreatedAt = DateTimeOffset.Now
            };
            liveAssistant = new ChatMessageViewModel(liveRecord, character.AvatarPath);
            var liveAssistantAdded = false;
            var previewActive = 1;

            void PublishPreview(string preview)
            {
                if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                _ = dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    if (Volatile.Read(ref previewActive) == 0) return;
                    if (!liveAssistantAdded)
                    {
                        Messages.Add(liveAssistant);
                        liveAssistantAdded = true;
                        IsAssistantTyping = false;
                    }
                    liveVariant.Content = preview;
                        liveAssistant.RefreshStreamingPreview();
                }));
            }

            var assistantText = await Task.Run(async () =>
            {
                var buffer = new StringBuilder();
                var lastPreviewAt = 0L;
                await foreach (var chunk in GenerateAsync(character, chat, text, CancellationToken.None).ConfigureAwait(false))
                {
                    buffer.Append(chunk);
                    var now = Environment.TickCount64;
                    if (now - lastPreviewAt >= 160)
                    {
                        lastPreviewAt = now;
                        PublishPreview(buffer.ToString());
                    }
                }
                return buffer.ToString();
            });

            Interlocked.Exchange(ref previewActive, 0);
            if (string.IsNullOrWhiteSpace(assistantText)) assistantText = "Модель не вернула текст.";
            await Task.Run(() => _stateVariables.ApplyFromResponseAsync(character.Id, chat.Id, assistantText));
            RefreshStateVariableValues();
            assistantText = _stateVariables.RemoveStateBlocks(assistantText);
            assistantText = SceneResponseFormatter.NormalizeRoleplayLayout(assistantText, character.UseRoleplayResponseFormatting);

            var normalizedReply = AppLog.NormalizeForComparison(assistantText);
            var exactRecentMatch = chat.Messages.Where(message => message.Role == SoulMessageRole.Assistant)
                .Select(message => message.Variants.FirstOrDefault(variant => variant.Id == message.CurrentVariantId)?.Content ?? message.Variants.FirstOrDefault()?.Content ?? string.Empty)
                .Any(previous => string.Equals(AppLog.NormalizeForComparison(previous), normalizedReply, StringComparison.Ordinal));
            AppLog.Write($"CHAT_RESPONSE character={character.Id} chat={chat.Id} chars={assistantText.Length} hash={AppLog.Fingerprint(assistantText)} exactRecentMatch={exactRecentMatch} preview=«{AppLog.Preview(assistantText)}»");
            var assistant = await Task.Run(() => _library.AddMessageAsync(character.Id, chat.Id, SoulMessageRole.Assistant, character.Name, assistantText));
            chat.Messages.Add(assistant);
            // Оставляем тот же визуальный элемент, который показывал потоковый текст. Раньше он
            // удалялся, а затем создавался заново, из-за чего лента мигала и прыгала в конце ответа.
            if (!liveAssistantAdded)
            {
                liveVariant.Content = assistantText;
                Messages.Add(liveAssistant);
                liveAssistantAdded = true;
            }
            liveAssistant.AdoptPersistedMessage(assistant);
            chat.UpdatedAt = assistant.CreatedAt;
            if (IsHomePage) RebuildHomeCards();
            RebuildConversationItems();
            _ = ScheduleCognitiveMaintenanceAfterReplyAsync(character.Id, chat.Id);
            Status = "Готово.";
        }
        catch (Exception ex)
        {
            if (liveAssistant is not null) Messages.Remove(liveAssistant);
            HandleError("Ошибка диалога", ex);
        }
        finally
        {
            IsAssistantTyping = false;
            IsBusy = false;
        }
    }

    private async IAsyncEnumerable<string> GenerateAsync(SoulCharacter character, SoulChat chat, string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, bool isContinuation = false)
    {
        var generationId = Guid.NewGuid().ToString("N")[..12];
        var settings = await BuildLlamaSettingsAsync();
        var mode = isContinuation ? "continuation" : "user_turn";
        var commandLog = isContinuation
            ? "directorCommand=«*continue*»"
            : $"userLen={text.Length} userHash={AppLog.Fingerprint(text)} userPreview=«{AppLog.Preview(text)}»";
        AppLog.Write($"GEN {generationId} BEGIN mode={mode} character={character.Id} chat={chat.Id} {commandLog}");
        var context = await _store.ReadAsync(root =>
        {
            var storedCharacter = root.Characters?.FirstOrDefault(x => x is not null && x.Id == character.Id) ?? character;
            storedCharacter.Chats ??= [];
            storedCharacter.LorebookIds ??= [];
            var storedChat = storedCharacter.Chats.FirstOrDefault(x => x is not null && x.Id == chat.Id) ?? chat;
            storedChat.Messages ??= [];
            storedChat.Memory ??= new SoulMemoryBundle();
            storedChat.Memory.Topics ??= [];
            var persona = storedCharacter.SelectedPersonaId is null ? null : root.Personas?.FirstOrDefault(x => x is not null && x.Id == storedCharacter.SelectedPersonaId);
            var preset = storedCharacter.SelectedPromptPresetId is null ? null : root.PromptPresets?.FirstOrDefault(x => x is not null && x.Id == storedCharacter.SelectedPromptPresetId);
            var books = (root.Lorebooks ?? []).Where(x => x is not null && storedCharacter.LorebookIds.Contains(x.Id)).ToList();
            var promptUserMessage = isContinuation ? string.Empty : text;
            var topics = MemoryTopicSelector.Select(storedChat.Memory.Topics, promptUserMessage);
            return _promptEngine.Build(new PromptBuildRequest(
                storedCharacter,
                storedChat,
                persona,
                preset,
                books,
                topics,
                promptUserMessage,
                settings.ContextSize,
                settings.MaxTokens,
                IncludeSoulMemory: storedCharacter.CognitiveArchitectureEnabled && storedCharacter.SoulMemoryEnabled,
                IncludeAutoSummary: storedCharacter.CognitiveArchitectureEnabled && storedCharacter.AutoSummaryEnabled,
                ExcludeLastUserMessage: !isContinuation,
                AppendUserMessage: !isContinuation,
                IsContinuation: isContinuation));
        }, token);
        var promptText = string.Join("\n", context.Messages.Select(message => $"{message.role}:{message.content}"));
        var loreDiagnostics = context.Diagnostics.Where(item => item.Category == "lore").Select(item => item.Text).ToList();
        AppLog.Write($"GEN {generationId} PROMPT messages={context.Messages.Count} chars={promptText.Length} hash={AppLog.Fingerprint(promptText)} historyRoles=[{string.Join(',', context.Messages.Select(message => message.role))}] diagnostics={context.Diagnostics.Count} loreActivated={loreDiagnostics.Count} lore=[{string.Join(" | ", loreDiagnostics)}]");
        var chunkCount = 0;
        var outputLength = 0;
        await foreach (var chunk in _llama.GenerateFromMessagesAsync(settings, context.Messages, token, generationId))
        {
            chunkCount++;
            outputLength += chunk.Length;
            yield return chunk;
        }
        AppLog.Write($"GEN {generationId} STREAM_CONSUMED chunks={chunkCount} chars={outputLength}");
    }

    private async Task<string> AskFromNetworkAsync(string characterId, string chatId, string message, CancellationToken token)
    {
        if (!Guid.TryParse(characterId, out var id)) throw new InvalidOperationException("Некорректный персонаж.");
        var character = await _library.GetCharacterAsync(id) ?? throw new InvalidOperationException("Персонаж не найден.");
        if (character.Chats.Count == 0) throw new InvalidOperationException("У выбранного персонажа пока нет чатов.");
        var chat = Guid.TryParse(chatId, out var selectedChatId)
            ? character.Chats.FirstOrDefault(x => x.Id == selectedChatId)
            : null;
        chat ??= character.Chats.FirstOrDefault(x => x.Id == character.CurrentChatId) ?? character.Chats.First();
        await _library.AddMessageAsync(character.Id, chat.Id, SoulMessageRole.User, "Вы", message, token: token);
        var reply = "";
        await foreach (var chunk in GenerateAsync(character, chat, message, token)) reply += chunk;
        await _stateVariables.ApplyFromResponseAsync(character.Id, chat.Id, reply, token);
        reply = _stateVariables.RemoveStateBlocks(reply);
        // Network clients bypass SendAsync, where the desktop applies this presentation pass.
        // Normalize before persisting so mobile, embedded web and Windows read the same layout.
        reply = SceneResponseFormatter.NormalizeRoleplayLayout(reply, character.UseRoleplayResponseFormatting);
        AppLog.Write($"NETWORK_CHAT_RESPONSE_FORMATTED character={character.Id:N} chat={chat.Id:N} enabled={character.UseRoleplayResponseFormatting} chars={reply.Length}");
        await _library.AddMessageAsync(character.Id, chat.Id, SoulMessageRole.Assistant, character.Name, reply, token: token);
        await ScheduleCognitiveMaintenanceAsync(character.Id, chat.Id);
        return reply;
    }

    private async Task UpdateCurrentMemoryAsync()
    {
        if (SelectedCharacter is null || SelectedChat is null) return;
        if (!SelectedCharacterCognitiveArchitectureEnabled || !SelectedCharacter.SoulMemoryEnabled)
        {
            Status = "Soul Memory отключена для выбранного персонажа. Включите её в карточке и сохраните настройки памяти.";
            return;
        }
        try
        {
            _cognitiveScheduler.Cancel(SelectedCharacter.Id, SelectedChat.Id);
            IsBusy = true;
            var result = await AppServices.SoulMemory.UpdateAfterConversationAsync(SelectedCharacter.Id, SelectedChat.Id, CompleteForMemoryAsync, force: true, intervalMessages: SelectedCharacter.SoulMemoryIntervalMessages, preset: SelectedCharacter.SoulMemoryPreset);
            await ReloadCharactersAsync(SelectedCharacter.Id);
            Status = result.Status;
        }
        catch (Exception ex) { HandleError("Не удалось обновить Soul Memory", ex); }
        finally { IsBusy = false; }
    }

    private async Task UpdateCurrentSummaryAsync()
    {
        if (SelectedCharacter is null || SelectedChat is null) return;
        if (!SelectedCharacterCognitiveArchitectureEnabled || !SelectedCharacter.AutoSummaryEnabled)
        {
            Status = "Auto-Summary отключено для выбранного персонажа. Включите его в карточке и сохраните настройки памяти.";
            return;
        }
        try
        {
            _cognitiveScheduler.Cancel(SelectedCharacter.Id, SelectedChat.Id);
            IsBusy = true;
            var result = await AppServices.Summaries.UpdateAsync(SelectedCharacter.Id, SelectedChat.Id, CompleteForMemoryAsync, force: true, intervalMessages: SelectedCharacter.AutoSummaryIntervalMessages);
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

    private async Task RefreshCognitiveUiAsync(Guid characterId)
    {
        if (Application.Current?.Dispatcher is null) return;
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (SelectedCharacter?.Id == characterId)
                await ReloadCharactersAsync(characterId);
        }).Task.Unwrap();
    }

    private void ReportCognitiveBackground(string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CognitiveBackgroundStatus = message;
            return;
        }
        _ = dispatcher.BeginInvoke(new Action(() => CognitiveBackgroundStatus = message));
    }

    private async Task<string> CompleteForMemoryAsync(IReadOnlyList<LlamaMessage> messages, CancellationToken token)
    {
        var settings = await BuildLlamaSettingsAsync();
        settings.MaxTokens = Math.Clamp(Math.Min(settings.MaxTokens, 960), 128, 960);
        settings.Temperature = Math.Min(settings.Temperature, 0.3d);
        AppLog.Write($"COGNITIVE_MAINTENANCE_REQUEST messages={messages.Count} maxTokens={settings.MaxTokens} temperature={settings.Temperature:0.###}");
        var answer = new System.Text.StringBuilder();
        await foreach (var chunk in _llama.GenerateFromMessagesAsync(settings, messages, token)) answer.Append(chunk);
        return answer.ToString();
    }

    private async Task SearchModelsAsync()
    {
        try
        {
            IsBusy = true;
            ModelDownloadStatus = "Ищу GGUF-модели на Hugging Face…";
            var results = await _modelsHub.SearchAsync(ModelSearchQuery);
            ModelSearchResults.Clear();
            foreach (var result in results) ModelSearchResults.Add(result);
            ModelFiles.Clear();
            SelectedModelResult = ModelSearchResults.FirstOrDefault();
            ModelDownloadStatus = results.Count == 0 ? "Поиск не дал результатов." : $"Найдено репозиториев: {results.Count}. Выберите модель.";
        }
        catch (Exception ex) { HandleError("Не удалось выполнить поиск моделей", ex); }
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

    private Task UseInstalledModelAsync() => SelectInstalledModelAsync(SelectedInstalledModel);

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

    private async Task SaveModelSettingsAsync()
    {
        try
        {
            NormalizeDiscreteGenerationLimits();
            await _store.MutateAsync(root =>
            {
                var p = root.Preferences;
                p.ActiveBackend = LlamaOptions.EngineBackend; p.LlamaPort = LlamaOptions.LlamaPort; p.ContextSize = LlamaOptions.ContextSize; p.MaxTokens = LlamaOptions.MaxTokens;
                p.Temperature = LlamaOptions.Temperature; p.TopP = LlamaOptions.TopP; p.TopK = LlamaOptions.TopK;
                p.GpuLayers = LlamaOptions.GpuLayers; p.FlashAttention = LlamaOptions.FlashAttention; p.UseMlock = LlamaOptions.UseMlock; p.UseMmap = LlamaOptions.UseMmap;
                p.KvCacheType = LlamaOptions.KvCacheType; p.CpuThreads = LlamaOptions.CpuThreads; p.CpuMoeLayers = LlamaOptions.CpuMoeLayers;
                p.BatchSize = LlamaOptions.BatchSize; p.ParallelSlots = LlamaOptions.ParallelSlots; p.ChatTemplate = LlamaOptions.ChatTemplate;
                p.ReasoningMode = LlamaOptions.ReasoningMode; p.ReasoningBudget = LlamaOptions.ReasoningBudget;
                p.FrequencyPenalty = LlamaOptions.FrequencyPenalty; p.PresencePenalty = LlamaOptions.PresencePenalty; p.EnableAdvancedSampling = LlamaOptions.EnableAdvancedSampling; p.MinP = LlamaOptions.MinP;
                p.DynamicTemperatureMin = LlamaOptions.DynamicTemperatureMin; p.DynamicTemperatureMax = LlamaOptions.DynamicTemperatureMax; p.DynamicTemperatureExponent = LlamaOptions.DynamicTemperatureExponent;
                p.XtcProbability = LlamaOptions.XtcProbability; p.XtcThreshold = LlamaOptions.XtcThreshold;
                p.DryMultiplier = LlamaOptions.DryMultiplier; p.DryBase = LlamaOptions.DryBase; p.DryAllowedLength = LlamaOptions.DryAllowedLength;
                p.StopStrings = LlamaOptions.StopStrings; p.ExtraLlamaArguments = LlamaOptions.ExtraArguments;
            }, "save_model_settings");
            Status = "Расширенные настройки llama.cpp сохранены рядом с программой.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить настройки модели", ex); }
    }

    private void SelectOptionsTab(string? tab)
    {
        _optionsTab = string.Equals(tab, "appearance", StringComparison.OrdinalIgnoreCase)
            ? "appearance"
            : string.Equals(tab, "mobile", StringComparison.OrdinalIgnoreCase) ? "mobile" : "llm";
        OnPropertyChanged(nameof(IsLlmOptionsTab));
        OnPropertyChanged(nameof(IsAppearanceOptionsTab));
        OnPropertyChanged(nameof(IsMobileOptionsTab));
    }

    private void SetChatAppearanceColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var parts = value.Split('|', 2);
        if (parts.Length != 2 || !parts[1].StartsWith('#')) return;
        switch (parts[0])
        {
            case "TextColor": ChatAppearance.TextColor = parts[1]; break;
            case "ActionColor": ChatAppearance.ActionColor = parts[1]; break;
            case "QuoteColor": ChatAppearance.QuoteColor = parts[1]; break;
            case "CodeColor": ChatAppearance.CodeColor = parts[1]; break;
            case "AssistantBubbleColor": ChatAppearance.AssistantBubbleColor = parts[1]; break;
            case "UserBubbleColor": ChatAppearance.UserBubbleColor = parts[1]; break;
            case "ChatBackgroundColor": ChatAppearance.ChatBackgroundColor = parts[1]; break;
            default: return;
        }
        _ = SaveChatAppearanceAsync();
    }

    private async Task SaveChatAppearanceAsync()
    {
        try
        {
            await _store.MutateAsync(root => root.Preferences.ChatAppearance = ChatAppearance.Clone(), "save_chat_appearance");
            Status = "Оформление чата сохранено рядом с программой.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить оформление чата", ex); }
    }

    private void ResetChatAppearance()
    {
        ChatAppearance = new ChatAppearanceSettings();
        _ = SaveChatAppearanceAsync();
        Status = "Оформление сброшено к стандартному виду и сохранено автоматически.";
    }

    private void LoadLlamaOptions(AppPreferences p)
    {
        LlamaOptions.EngineBackend = p.ActiveBackend; LlamaOptions.LlamaPort = p.LlamaPort; LlamaOptions.ContextSize = p.ContextSize; LlamaOptions.MaxTokens = p.MaxTokens;
        LlamaOptions.Temperature = p.Temperature; LlamaOptions.TopP = p.TopP; LlamaOptions.TopK = p.TopK;
        LlamaOptions.GpuLayers = p.GpuLayers; LlamaOptions.FlashAttention = p.FlashAttention; LlamaOptions.UseMlock = p.UseMlock; LlamaOptions.UseMmap = p.UseMmap;
        LlamaOptions.KvCacheType = p.KvCacheType; LlamaOptions.CpuThreads = p.CpuThreads; LlamaOptions.CpuMoeLayers = p.CpuMoeLayers;
        LlamaOptions.BatchSize = p.BatchSize; LlamaOptions.ParallelSlots = p.ParallelSlots; LlamaOptions.ChatTemplate = p.ChatTemplate;
        LlamaOptions.ReasoningMode = p.ReasoningMode; LlamaOptions.ReasoningBudget = p.ReasoningBudget;
        LlamaOptions.FrequencyPenalty = p.FrequencyPenalty; LlamaOptions.PresencePenalty = p.PresencePenalty; LlamaOptions.EnableAdvancedSampling = p.EnableAdvancedSampling; LlamaOptions.MinP = p.MinP;
        LlamaOptions.DynamicTemperatureMin = p.DynamicTemperatureMin; LlamaOptions.DynamicTemperatureMax = p.DynamicTemperatureMax; LlamaOptions.DynamicTemperatureExponent = p.DynamicTemperatureExponent;
        LlamaOptions.XtcProbability = p.XtcProbability; LlamaOptions.XtcThreshold = p.XtcThreshold;
        LlamaOptions.DryMultiplier = p.DryMultiplier; LlamaOptions.DryBase = p.DryBase; LlamaOptions.DryAllowedLength = p.DryAllowedLength;
        LlamaOptions.StopStrings = p.StopStrings; LlamaOptions.ExtraArguments = p.ExtraLlamaArguments;
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
                var marker = message.LastIndexOf('%');
                if (marker > 0)
                {
                    var start = marker - 1;
                    while (start >= 0 && char.IsDigit(message[start])) start--;
                    if (int.TryParse(message[(start + 1)..marker], out var percent)) SetupProgressPercent = percent;
                }
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

    private async Task StartFromSetupAsync()
    {
        await StartModelAsync();
        if (!_llama.IsStartedByApplication) return;
        await FinishInitialSetupAsync();
        CurrentPage = "Chat";
        Status = "Локальная модель запущена. Открыт чат.";
    }

    private async Task FinishInitialSetupAsync()
    {
        try
        {
            await _store.MutateAsync(root => root.Preferences.InitialSetupCompleted = true, "complete_initial_setup");
            IsInitialSetupVisible = false;
            CurrentPage = "Home";
            Status = string.IsNullOrWhiteSpace(ModelPath)
                ? "Начальная настройка закрыта. Движок и модель можно установить позже в Models Hub."
                : "Начальная настройка завершена. Можно выбрать персонажа и начать чат.";
        }
        catch (Exception ex) { HandleError("Не удалось завершить начальную настройку", ex); }
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

    private RecommendedModel WithInstallationState(RecommendedModel model)
    {
        var installed = InstalledModels.FirstOrDefault(local => IsRecommendedFileMatch(model, local));
        return model with { InstalledFileName = installed is null ? null : Path.GetFileName(installed.LocalPath) };
    }

    private static bool IsRecommendedFileMatch(RecommendedModel recommendation, SoulModelInstallation local)
    {
        var localFile = Path.GetFileName(local.LocalPath);
        if (string.IsNullOrWhiteSpace(localFile)) return false;
        var hasQuant = localFile.Contains(recommendation.OptimalQuant, StringComparison.OrdinalIgnoreCase);
        if (!hasQuant) return false;
        if (local.Metadata.TryGetValue("repository_id", out var repository) &&
            string.Equals(repository, recommendation.RepositoryId, StringComparison.OrdinalIgnoreCase)) return true;
        var repoTail = recommendation.RepositoryId.Split('/').LastOrDefault() ?? recommendation.RepositoryId;
        var normalizedRepo = NormalizeModelFileToken(repoTail);
        var normalizedFile = NormalizeModelFileToken(localFile);
        return normalizedRepo.Length >= 6 && (normalizedFile.Contains(normalizedRepo, StringComparison.Ordinal) || normalizedRepo.Contains(normalizedFile[..Math.Min(normalizedFile.Length, normalizedRepo.Length)], StringComparison.Ordinal));
    }

    private static string NormalizeModelFileToken(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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
        var text = $"Скачивание {request.File.Path}: {progress.Display}";
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
        SetActiveDownloadStatus("Ставлю загрузку на паузу: текущий фрагмент сохраняется…");
        _modelDownloadCts.Cancel();
    }

    private void CancelModelDownload()
    {
        var request = _resumableDownload;
        if (request is null) return;
        _downloadCancelRequested = true;
        if (_modelDownloadCts is not null && !_modelDownloadCts.IsCancellationRequested)
        {
            SetActiveDownloadStatus("Отменяю загрузку: частичный файл будет удалён…");
            _modelDownloadCts.Cancel();
            return;
        }

        var initialSetup = request.IsInitialSetup;
        _modelsHub.DiscardPartialDownload(request.RepositoryId, request.File);
        _resumableDownload = null;
        CanResumeModelDownload = false;
        SetDownloadStatus("Загрузка отменена. Частичный файл удалён из SoulExeData.", initialSetup);
        Status = "Загрузка модели отменена.";
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
        ModelRepository = "ggml-org/Qwen3.5-0.8B-GGUF";
        ModelPath = "";
        OnPropertyChanged(nameof(ModelSourceText));
        Status = "Выбрана компактная стартовая модель. При первом запуске llama.cpp скачает её автоматически.";
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

    private async Task SaveMobileAccessAsync()
    {
        await _store.MutateAsync(root =>
        {
            root.Preferences.MobileAccessUsername = string.IsNullOrWhiteSpace(MobileAccessUsername) ? "admin" : MobileAccessUsername;
            root.Preferences.MobileAccessPassword = string.IsNullOrEmpty(MobileAccessPassword) ? "admin" : MobileAccessPassword;
            root.Preferences.LocalWebServerEnabled = StartMobileServerOnLaunch;
        }, "save_mobile_access");
    }

    private async Task StartNetworkOnLaunchAsync()
    {
        if (!StartMobileServerOnLaunch) return;

        try
        {
            if (string.IsNullOrWhiteSpace(MobileAccessUsername) || string.IsNullOrEmpty(MobileAccessPassword))
            {
                Status = "Автозапуск мобильного сервера пропущен: укажите логин и пароль в настройках «Мобильный».";
                return;
            }

            await _network.StartAsync(MobileServerPort);
            Status = "Мобильный сервер запущен автоматически. Откройте адрес из раздела «Мобильный» на телефоне.";
            OnPropertyChanged(nameof(NetworkRunning));
            OnPropertyChanged(nameof(NetworkAccessUrl));
            OnPropertyChanged(nameof(NetworkAccessToken));
        }
        catch (Exception ex)
        {
            Status = $"Не удалось автоматически запустить мобильный сервер: {ex.Message}";
            OnPropertyChanged(nameof(NetworkRunning));
        }
    }

    private async Task ToggleNetworkAsync()
    {
        try
        {
            IsBusy = true;
            if (_network.IsRunning) { await _network.StopAsync(); Status = "Мобильный веб-клиент остановлен."; }
            else
            {
                if (string.IsNullOrWhiteSpace(MobileAccessUsername) || string.IsNullOrEmpty(MobileAccessPassword)) throw new InvalidOperationException("Укажите логин и пароль для мобильного входа.");
                await SaveMobileAccessAsync();
                await _network.StartAsync(MobileServerPort);
                Status = "Мобильный веб-клиент запущен. Откройте адрес на телефоне и войдите по логину и паролю.";
            }
            OnPropertyChanged(nameof(NetworkRunning));
            OnPropertyChanged(nameof(NetworkAccessUrl));
            OnPropertyChanged(nameof(NetworkAccessToken));
        }
        catch (Exception ex) { HandleError("Не удалось изменить состояние веб-клиента", ex); }
        finally { IsBusy = false; }
    }

    private void ChooseServer()
    {
        var dialog = new OpenFileDialog { Filter = "llama-server.exe|llama-server.exe|Executable files|*.exe" };
        if (dialog.ShowDialog() == true) ServerPath = dialog.FileName;
    }

    private async Task ChooseModelAsync()
    {
        var dialog = new OpenFileDialog { Filter = "GGUF model|*.gguf|All files|*.*" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsBusy = true;
            var source = dialog.FileName;
            var localDirectory = Path.Combine(AppServices.Paths.ModelDirectory, "manual");
            Directory.CreateDirectory(localDirectory);
            var destination = Path.Combine(localDirectory, Path.GetFileName(source));
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            {
                Status = $"Копирую GGUF в SoulExeData: {Path.GetFileName(source)}…";
                await using var input = File.OpenRead(source);
                await using var output = File.Create(destination);
                await input.CopyToAsync(output);
            }
            ModelRepository = "";
            ModelPath = destination;
            await _modelsHub.RegisterExistingModelAsync(destination);
            await RefreshInstalledModelsAsync();
            OnPropertyChanged(nameof(ModelSourceText));
            Status = $"GGUF сохранён локально: {Path.GetFileName(destination)}";
        }
        catch (Exception ex) { HandleError("Не удалось добавить GGUF в библиотеку", ex); }
        finally { IsBusy = false; }
    }

    private void ChooseAvatar()
    {
        if (SelectedCharacter is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var extension = Path.GetExtension(dialog.FileName);
            var target = Path.Combine(AppServices.Paths.AvatarDirectory, $"{SelectedCharacter.Id}{extension}");
            File.Copy(dialog.FileName, target, overwrite: true);
            SelectedCharacter.AvatarPath = target;
            _ = SaveCharacterAsync();
            OnPropertyChanged(nameof(SelectedCharacter));
        }
        catch (Exception ex) { HandleError("Не удалось сохранить фото-аватар", ex); }
    }

    private void ChoosePersonaAvatar()
    {
        if (SelectedPersona is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var extension = Path.GetExtension(dialog.FileName);
            var target = Path.Combine(AppServices.Paths.AvatarDirectory, $"persona_{SelectedPersona.Id}{extension}");
            File.Copy(dialog.FileName, target, overwrite: true);
            SelectedPersona.AvatarPath = target;
            _ = SavePersonaAsync();
            OnPropertyChanged(nameof(SelectedPersona));
        }
        catch (Exception ex) { HandleError("Не удалось сохранить фото-аватар персоны", ex); }
    }

    private async Task SavePreferencesAsync()
    {
        await _store.MutateAsync(root =>
        {
            root.Preferences.LlamaServerPath = ServerPath;
            root.Preferences.ModelPath = ModelPath;
            root.Preferences.ModelHuggingFaceRepository = ModelRepository;
        }, "save_preferences");
    }

    private async Task<AppSettings> BuildLlamaSettingsAsync()
    {
        var data = await _store.ReadAsync(root => root.Preferences);
        return new AppSettings
        {
            LlamaServerPath = ServerPath,
            ModelPath = ModelPath,
            ModelHuggingFaceRepository = ModelRepository,
            PreferredHost = "127.0.0.1",
            LlamaPort = LlamaOptions.LlamaPort,
            NetworkPort = data.NetworkPort,
            ContextSize = LlamaOptions.ContextSize,
            MaxTokens = LlamaOptions.MaxTokens,
            Temperature = LlamaOptions.Temperature,
            TopP = LlamaOptions.TopP,
            TopK = LlamaOptions.TopK,
            GpuLayers = LlamaOptions.GpuLayers,
            FlashAttention = LlamaOptions.FlashAttention,
            UseMlock = LlamaOptions.UseMlock,
            UseMmap = LlamaOptions.UseMmap,
            KvCacheType = LlamaOptions.KvCacheType,
            CpuThreads = LlamaOptions.CpuThreads,
            CpuMoeLayers = LlamaOptions.CpuMoeLayers,
            BatchSize = LlamaOptions.BatchSize,
            ParallelSlots = LlamaOptions.ParallelSlots,
            ChatTemplate = LlamaOptions.ChatTemplate,
            ReasoningMode = LlamaOptions.ReasoningMode,
            ReasoningBudget = LlamaOptions.ReasoningBudget,
            FrequencyPenalty = LlamaOptions.FrequencyPenalty,
            PresencePenalty = LlamaOptions.PresencePenalty,
            EnableAdvancedSampling = LlamaOptions.EnableAdvancedSampling,
            MinP = LlamaOptions.MinP,
            DynamicTemperatureMin = LlamaOptions.DynamicTemperatureMin,
            DynamicTemperatureMax = LlamaOptions.DynamicTemperatureMax,
            DynamicTemperatureExponent = LlamaOptions.DynamicTemperatureExponent,
            XtcProbability = LlamaOptions.XtcProbability,
            XtcThreshold = LlamaOptions.XtcThreshold,
            DryMultiplier = LlamaOptions.DryMultiplier,
            DryBase = LlamaOptions.DryBase,
            DryAllowedLength = LlamaOptions.DryAllowedLength,
            StopStrings = LlamaOptions.StopStrings,
            ExtraArguments = LlamaOptions.ExtraArguments
        };
    }

    private void HandleError(string context, Exception exception)
    {
        AppLog.Write(context, exception);
        Status = IsContextCapacityError(exception)
            ? $"{context}: контекст модели переполнен. SoulExe безопасно остановил текущую операцию; новая версия сокращает старую историю автоматически."
            : $"{context}: {exception.Message}";
    }

    private static bool IsContextCapacityError(Exception exception) =>
        exception.ToString().Contains("exceed_context_size_error", StringComparison.OrdinalIgnoreCase)
        || exception.ToString().Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);

    private async Task PauseSceneAfterContextCapacityErrorAsync(Guid sceneId, CancellationToken token = default)
    {
        CancelSceneTimer();
        StopNetworkSceneLoop(sceneId);
        try { await _scenes.SetStatusAsync(sceneId, "paused", token: token); }
        catch (Exception pauseException) { AppLog.Write($"Не удалось поставить сцену {sceneId:N} на паузу после превышения контекста.", pauseException); }

        await UpdateSceneUiAsync(() =>
        {
            if (SelectedScene?.Id != sceneId) return;
            SelectedScene.Status = "paused";
            OnPropertyChanged(nameof(SelectedScene));
            OnPropertyChanged(nameof(SceneStartPauseText));
            RaiseSceneCommands();
        });
    }

    private void RaiseAllCommands()
    {
        SendCommand.RaiseCanExecuteChanged(); ContinueChatCommand.RaiseCanExecuteChanged(); StartModelCommand.RaiseCanExecuteChanged(); StopModelCommand.RaiseCanExecuteChanged(); ToggleModelStartStopCommand.RaiseCanExecuteChanged(); InstallEngineCommand.RaiseCanExecuteChanged(); PauseModelDownloadCommand.RaiseCanExecuteChanged(); ResumeModelDownloadCommand.RaiseCanExecuteChanged(); ToggleModelDownloadCommand.RaiseCanExecuteChanged(); CancelModelDownloadCommand.RaiseCanExecuteChanged();
        UseStarterModelCommand.RaiseCanExecuteChanged(); ToggleNetworkCommand.RaiseCanExecuteChanged(); ChooseModelCommand.RaiseCanExecuteChanged(); SetupInstallEngineCommand.RaiseCanExecuteChanged(); SetupSelectAndInstallBackendCommand.RaiseCanExecuteChanged(); SetupDownloadRecommendedCommand.RaiseCanExecuteChanged(); SkipInitialSetupCommand.RaiseCanExecuteChanged(); NextInitialSetupStepCommand.RaiseCanExecuteChanged(); SetupStartChatCommand.RaiseCanExecuteChanged(); AddCharacterCommand.RaiseCanExecuteChanged(); ToggleCharacterGeneratorCommand.RaiseCanExecuteChanged(); OpenCharacterCreationDialogCommand.RaiseCanExecuteChanged(); SelectCharacterCreationModeCommand.RaiseCanExecuteChanged(); CreateCharacterWithNameCommand.RaiseCanExecuteChanged(); GenerateCharacterFromIdeaCommand.RaiseCanExecuteChanged(); ImportCharacterCommand.RaiseCanExecuteChanged(); ImportSoulOfWaifuCommand.RaiseCanExecuteChanged(); ExportCharacterCommand.RaiseCanExecuteChanged();
        DeleteCharacterCommand.RaiseCanExecuteChanged(); CreateSceneCommand.RaiseCanExecuteChanged(); SaveSceneCommand.RaiseCanExecuteChanged(); DeleteSceneCommand.RaiseCanExecuteChanged(); StartSceneCommand.RaiseCanExecuteChanged(); PauseSceneCommand.RaiseCanExecuteChanged(); ToggleSceneStartPauseCommand.RaiseCanExecuteChanged(); NextSceneTurnCommand.RaiseCanExecuteChanged(); ChooseSceneSpeakerCommand.RaiseCanExecuteChanged(); AddDirectorEventCommand.RaiseCanExecuteChanged(); FinishSceneCommand.RaiseCanExecuteChanged(); OpenCharacterChatCommand.RaiseCanExecuteChanged(); OpenCharacterEditorCommand.RaiseCanExecuteChanged(); ConfirmDeleteCharacterCommand.RaiseCanExecuteChanged(); ConfirmCharacterDeleteCommand.RaiseCanExecuteChanged(); AddChatCommand.RaiseCanExecuteChanged(); ConfirmNewChatForCharacterCommand.RaiseCanExecuteChanged();         ToggleChatPinnedCommand.RaiseCanExecuteChanged(); ToggleConversationPinnedCommand.RaiseCanExecuteChanged(); BeginRenameConversationCommand.RaiseCanExecuteChanged(); DeleteConversationCommand.RaiseCanExecuteChanged(); ConfirmRenameSceneCommand.RaiseCanExecuteChanged(); ToggleChatMessageSearchCommand.RaiseCanExecuteChanged(); ToggleSceneMessageSearchCommand.RaiseCanExecuteChanged(); DeleteChatCommand.RaiseCanExecuteChanged(); SaveCharacterCommand.RaiseCanExecuteChanged(); ExpandCharacterFieldCommand.RaiseCanExecuteChanged(); PreviousVariantCommand.RaiseCanExecuteChanged(); NextVariantCommand.RaiseCanExecuteChanged(); BeginEditMessageCommand.RaiseCanExecuteChanged(); CancelEditMessageCommand.RaiseCanExecuteChanged(); SaveEditMessageCommand.RaiseCanExecuteChanged(); DeleteMessageCommand.RaiseCanExecuteChanged(); ContinueFromMessageCommand.RaiseCanExecuteChanged(); SearchModelsCommand.RaiseCanExecuteChanged(); DownloadSelectedModelCommand.RaiseCanExecuteChanged(); RefreshInstalledModelsCommand.RaiseCanExecuteChanged(); UseInstalledModelCommand.RaiseCanExecuteChanged(); SaveModelSettingsCommand.RaiseCanExecuteChanged(); SaveChatAppearanceCommand.RaiseCanExecuteChanged(); AddLorebookCommand.RaiseCanExecuteChanged(); DeleteLoreEntryCommand.RaiseCanExecuteChanged(); SaveLorebookCommand.RaiseCanExecuteChanged(); AddLoreEntryCommand.RaiseCanExecuteChanged(); AddPersonaCommand.RaiseCanExecuteChanged(); OpenPersonaEditorCommand.RaiseCanExecuteChanged(); SavePersonaCommand.RaiseCanExecuteChanged(); ConfirmDeletePersonaCommand.RaiseCanExecuteChanged(); DeletePersonaCommand.RaiseCanExecuteChanged(); ChoosePersonaAvatarCommand.RaiseCanExecuteChanged(); LoadGatewayTrendingCommand.RaiseCanExecuteChanged(); SearchGatewayCommand.RaiseCanExecuteChanged(); ImportGatewayAssetCommand.RaiseCanExecuteChanged(); LoadMoreGatewayCommand.RaiseCanExecuteChanged(); UpdateMemoryCommand.RaiseCanExecuteChanged(); UpdateSummaryCommand.RaiseCanExecuteChanged();
    }

    private void CopyNetworkAddress()
    {
        try
        {
            Clipboard.SetText(NetworkAccessUrl);
            Status = "Сетевой адрес для телефона скопирован в буфер обмена.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Не удалось скопировать сетевой адрес", ex);
            Status = "Не удалось скопировать адрес. Выделите его в поле и скопируйте вручную.";
        }
    }

    private static string GetLocalIp()
    {
        try
        {
            var preferred = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                  && adapter.NetworkInterfaceType is not System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                                  && adapter.NetworkInterfaceType is not System.Net.NetworkInformation.NetworkInterfaceType.Tunnel
                                  && adapter.GetIPProperties().GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                           && !System.Net.IPAddress.IsLoopback(address)
                                           && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));

            if (preferred is not null) return preferred.ToString();

            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                           && !System.Net.IPAddress.IsLoopback(address)
                                           && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async ValueTask DisposeAsync()
    {
        CancelSceneTimer();
        foreach (var sceneId in _networkSceneLoops.Keys) StopNetworkSceneLoop(sceneId);
        try
        {
            await _cognitiveScheduler.DisposeAsync();
            await _network.DisposeAsync();
            await _llama.DisposeAsync();
        }
        finally
        {
            _installer.Dispose();
        }
    }
}

public sealed class SceneMessageViewModel : INotifyPropertyChanged
{
    private string _content;
    private readonly bool _isLive;
    private readonly string _avatarPath;
    private bool _isSearchHighlighted;
    private SceneMessageViewModel(Guid id, string speakerName, string content, string time, bool isDirector, bool isFirstCharacter, bool isLive, string? avatarPath)
    {
        Id = id; SpeakerName = speakerName; _content = content; Time = time; IsDirector = isDirector; IsFirstCharacter = isFirstCharacter; _isLive = isLive; _avatarPath = avatarPath ?? "";
    }
    public SceneMessageViewModel(SoulSceneMessage message, Guid firstCharacterId, string? avatarPath = null)
        : this(message.Id, message.SpeakerName, message.Content, message.CreatedAt.ToString("HH:mm"), message.Kind == SoulSceneMessageKind.Director, message.Kind == SoulSceneMessageKind.Character && message.SpeakerCharacterId == firstCharacterId, false, avatarPath) { }
    public static SceneMessageViewModel Live(string speakerName, bool isFirstCharacter, string? avatarPath = null) => new(Guid.NewGuid(), speakerName, "", DateTime.Now.ToString("HH:mm"), false, isFirstCharacter, true, avatarPath);
    public Guid Id { get; }
    public string SpeakerName { get; }
    public string SpeakerInitials => string.IsNullOrWhiteSpace(SpeakerName)
        ? "?"
        : string.Concat(SpeakerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
    public string AvatarPath => _avatarPath;
    public string Content { get => _content; private set { if (_content == value) return; _content = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content))); } }
    public string Time { get; }
    public bool IsDirector { get; }
    public bool IsFirstCharacter { get; }
    public bool IsLive => _isLive;
    public bool IsSearchHighlighted => _isSearchHighlighted;
    public void SetSearchHighlighted(bool value)
    {
        if (_isSearchHighlighted == value) return;
        _isSearchHighlighted = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchHighlighted)));
    }
    public void Append(string chunk) => Content += chunk;
    public void SetContent(string value) => Content = value ?? "";
    public event PropertyChangedEventHandler? PropertyChanged;
}

internal sealed record ModelDownloadRequest(string RepositoryId, ModelHubFile File, string? RecommendationName, bool IsInitialSetup, bool IsRecommended);
internal sealed record GeneratedCharacterCard(string Name, string Title, string Description, string Personality, string Scenario, string SystemPrompt, string FirstMessage);

public sealed record GatewayCategoryOption(string Id, string Title, string Description);

public sealed class StateVariableContextItem
{
    public StateVariableContextItem(string displayName, string key, string valueJson, string variableType)
    {
        DisplayName = displayName;
        Key = key;
        ValueJson = valueJson;
        VariableType = variableType;
    }

    public string DisplayName { get; }
    public string Key { get; }
    public string ValueJson { get; }
    public string VariableType { get; }
}

public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private readonly SoulMessage _record;
    private readonly string _avatarPath;
    private bool _isThoughtExpanded;
    private bool _isEditing;
    private bool _isSearchHighlighted;
    private bool _isActionMenuOpen;
    private bool _showDateSeparator;
    private string _dateSeparatorLabel = "";
    private string _editingContent = "";

    public ChatMessageViewModel(SoulMessage record, string? avatarPath = null)
    {
        _record = record;
        _avatarPath = avatarPath ?? "";
        ToggleThoughtCommand = new RelayCommand(_ => ToggleThought(), _ => HasThoughtContent);
    }

    public Guid MessageId => _record.Id;
    public string AuthorName => string.IsNullOrWhiteSpace(_record.AuthorName) ? (IsUser ? "Вы" : "Персонаж") : _record.AuthorName;
    public string AuthorInitials => string.Concat(AuthorName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
    public string AvatarPath => IsUser ? "" : _avatarPath;
    public string Content => CurrentVariant?.Content ?? "";
    public string VisibleContent => SplitThought(Content).Visible;
    public string ThoughtContent => SplitThought(Content).Thought;
    public bool HasThoughtContent => !IsUser && !string.IsNullOrWhiteSpace(ThoughtContent);
    public bool IsThoughtExpanded => _isThoughtExpanded;
    public bool IsEditing => _isEditing;
    public bool IsSearchHighlighted => _isSearchHighlighted;
    public bool IsActionMenuOpen
    {
        get => _isActionMenuOpen;
        set
        {
            if (_isActionMenuOpen == value) return;
            _isActionMenuOpen = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActionMenuOpen)));
        }
    }
    public string EditingContent
    {
        get => _editingContent;
        set
        {
            if (_editingContent == value) return;
            _editingContent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EditingContent)));
        }
    }
    public bool CanContinueFromHere => IsUser;
    public bool HasResponseVariants => !IsUser && VariantCount > 1;
    public string ThoughtToggleText => IsThoughtExpanded ? "▾  Скрыть мысли" : "▸  Показать мысли";
    public RelayCommand ToggleThoughtCommand { get; }
    public bool ShowDateSeparator { get => _showDateSeparator; set { if (_showDateSeparator == value) return; _showDateSeparator = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowDateSeparator))); } }
    public string DateSeparatorLabel { get => _dateSeparatorLabel; set { if (_dateSeparatorLabel == value) return; _dateSeparatorLabel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateSeparatorLabel))); } }
    public string Time => _record.CreatedAt.ToLocalTime().ToString("HH:mm");
    public bool IsUser => _record.Role == SoulMessageRole.User;
    public int VariantCount => _record.Variants.Count;
    public int CurrentVariantNumber => Math.Max(1, _record.Variants.FindIndex(x => x.Id == _record.CurrentVariantId) + 1);
    public bool CanMovePrevious => !IsUser && CurrentVariantNumber > 1;
    public bool CanMoveNext => !IsUser && CurrentVariantNumber < VariantCount;
    private SoulMessageVariant? CurrentVariant => _record.Variants.FirstOrDefault(x => x.Id == _record.CurrentVariantId) ?? _record.Variants.FirstOrDefault();
    public event PropertyChangedEventHandler? PropertyChanged;

    public SoulMessageVariant? GetAdjacentVariant(int direction)
    {
        var index = _record.Variants.FindIndex(x => x.Id == _record.CurrentVariantId);
        if (index < 0) index = 0;
        var target = index + direction;
        return target >= 0 && target < _record.Variants.Count ? _record.Variants[target] : null;
    }

    public void SelectVariant(Guid id)
    {
        _record.CurrentVariantId = id;
        _isThoughtExpanded = false;
        Refresh();
    }

    public void AdoptPersistedMessage(SoulMessage saved)
    {
        _record.Id = saved.Id;
        _record.SequenceNumber = saved.SequenceNumber;
        _record.Role = saved.Role;
        _record.AuthorName = saved.AuthorName;
        _record.CurrentVariantId = saved.CurrentVariantId;
        _record.Variants = saved.Variants;
        _record.Attachments = saved.Attachments;
        _record.CreatedAt = saved.CreatedAt;
        _record.EditedAt = saved.EditedAt;
        Refresh();
    }

    public void SetSearchHighlighted(bool value)
    {
        if (_isSearchHighlighted == value) return;
        _isSearchHighlighted = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSearchHighlighted)));
    }

    public void BeginEditing()
    {
        EditingContent = Content;
        _isEditing = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
    }

    public void CancelEditing()
    {
        _isEditing = false;
        EditingContent = "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditing)));
    }

    public void Refresh()
    {
        foreach (var name in new[] { nameof(MessageId), nameof(AuthorName), nameof(AuthorInitials), nameof(AvatarPath), nameof(Content), nameof(VisibleContent), nameof(ThoughtContent), nameof(HasThoughtContent), nameof(IsThoughtExpanded), nameof(IsEditing), nameof(IsSearchHighlighted), nameof(IsActionMenuOpen), nameof(EditingContent), nameof(Time), nameof(CanContinueFromHere), nameof(HasResponseVariants), nameof(ThoughtToggleText), nameof(VariantCount), nameof(CurrentVariantNumber), nameof(CanMovePrevious), nameof(CanMoveNext) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        ToggleThoughtCommand.RaiseCanExecuteChanged();
    }

    public void RefreshStreamingPreview()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasThoughtContent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtToggleText)));
        ToggleThoughtCommand.RaiseCanExecuteChanged();
    }

    private void ToggleThought()
    {
        _isThoughtExpanded = !_isThoughtExpanded;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsThoughtExpanded)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThoughtToggleText)));
    }

    private static (string Visible, string Thought) SplitThought(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return ("", "");
        var remaining = text;
        var thoughtParts = new List<string>();
        foreach (var tag in new[] { "think", "thinking", "thought", "reasoning" })
        {
            var open = $"<{tag}>";
            var close = $"</{tag}>";
            while (true)
            {
                var start = remaining.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                var end = remaining.IndexOf(close, start + open.Length, StringComparison.OrdinalIgnoreCase);
                if (end < 0) break;
                thoughtParts.Add(remaining[(start + open.Length)..end].Trim());
                remaining = remaining.Remove(start, end + close.Length - start);
            }
        }
        return (remaining.Trim(), string.Join("\n\n", thoughtParts.Where(x => !string.IsNullOrWhiteSpace(x))));
    }
}

public sealed record ChatCharacterSortOption(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class HomeCharacterCardViewModel
{
    private HomeCharacterCardViewModel(SoulCharacter? character, bool isAddCharacter)
    {
        Character = character;
        IsAddCharacter = isAddCharacter;
    }

    public HomeCharacterCardViewModel(SoulCharacter character) : this(character, false) { }
    public SoulCharacter? Character { get; }
    public bool IsAddCharacter { get; }
    public static HomeCharacterCardViewModel AddCard() => new(null, true);
}

public sealed record ChatMessageSearchResult(Guid MessageId, string AuthorName, string Content, DateTimeOffset CreatedAt)
{
    public string DisplayAuthor => string.IsNullOrWhiteSpace(AuthorName) ? "Персонаж" : AuthorName;
    public string Timestamp => CreatedAt.LocalDateTime.ToString("dd.MM · HH:mm");
    public string Preview
    {
        get
        {
            var prepared = string.Join(" ", (Content ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return prepared.Length <= 100 ? prepared : prepared[..99] + "…";
        }
    }
}

public sealed class ChatListItemViewModel : INotifyPropertyChanged
{
    private string _chatNameDraft;
    private bool _isRenaming;
    private bool _isActionMenuOpen;

    public ChatListItemViewModel(SoulCharacter character, SoulChat chat)
    {
        Character = character;
        Chat = chat;
        _chatNameDraft = chat.Name;
    }

    public SoulCharacter Character { get; }
    public SoulChat Chat { get; }
    public Guid CharacterId => Character.Id;
    public Guid ChatId => Chat.Id;
    public string CharacterName => Character.Name;
    public string AvatarPath => Character.AvatarPath;
    public string Initials => Character.Initials;
    public string ChatName => Chat.Name;
    public bool IsPinned => Chat.IsPinned;
    public string PinIcon => IsPinned ? "📌" : "";
    public string PinMenuText => IsPinned ? "📌  Открепить" : "📌  Закрепить";
    private SoulMessage? LastMessage => Chat.Messages?.OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.SequenceNumber).FirstOrDefault();
    public DateTimeOffset UpdatedAt => LastMessage?.CreatedAt ?? Chat.CreatedAt;
    public string LastMessagePreview
    {
        get
        {
            var message = LastMessage;
            if (message is null) return "Нет сообщений";
            var content = message.Variants?.FirstOrDefault(variant => variant.Id == message.CurrentVariantId)?.Content
                ?? message.Variants?.FirstOrDefault()?.Content
                ?? string.Empty;
            content = string.Join(" ", content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return content.Length <= 56 ? content : content[..55] + "…";
        }
    }
    public string LastMessageTime
    {
        get
        {
            var timestamp = LastMessage?.CreatedAt ?? UpdatedAt;
            return timestamp.LocalDateTime.Date == DateTime.Today
                ? timestamp.LocalDateTime.ToString("HH:mm")
                : timestamp.LocalDateTime.ToString("dd.MM");
        }
    }
    public bool CanDelete => true;
    public int MessageCount => Chat.Messages?.Count ?? 0;
    public bool MatchesSearch(string query) => string.IsNullOrWhiteSpace(query)
        || CharacterName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || ChatName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || LastMessagePreview.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    public string ChatNameDraft { get => _chatNameDraft; set { if (_chatNameDraft == value) return; _chatNameDraft = value; OnPropertyChanged(nameof(ChatNameDraft)); } }
        public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value) return;
            _isRenaming = value;
            OnPropertyChanged(nameof(IsRenaming));
        }
    }

    public bool IsActionMenuOpen
    {
        get => _isActionMenuOpen;
        set
        {
            if (_isActionMenuOpen == value) return;
            _isActionMenuOpen = value;
            OnPropertyChanged(nameof(IsActionMenuOpen));
        }
    }
    public void Refresh()
    {
        OnPropertyChanged(nameof(CharacterName));
        OnPropertyChanged(nameof(AvatarPath));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(ChatName));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(PinIcon));
        OnPropertyChanged(nameof(PinMenuText));
        OnPropertyChanged(nameof(UpdatedAt));
        OnPropertyChanged(nameof(LastMessagePreview));
        OnPropertyChanged(nameof(LastMessageTime));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(MessageCount));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}


public sealed class ConversationListItemViewModel
{
    private ConversationListItemViewModel(ChatListItemViewModel chatItem)
    {
        ChatItem = chatItem;
        Id = chatItem.ChatId;
        IsScene = false;
        Title = chatItem.CharacterName;
        SecondaryTitle = chatItem.ChatName;
        Preview = NormalizePreview(chatItem.LastMessagePreview);
        UpdatedAt = chatItem.UpdatedAt;
        IsPinned = chatItem.IsPinned;
        AvatarAPath = chatItem.AvatarPath;
        AvatarAInitials = chatItem.Initials;
        AvatarBPath = "";
        AvatarBInitials = "";
    }

    private ConversationListItemViewModel(SoulScene scene, SoulCharacter? first, SoulCharacter? second)
    {
        Scene = scene;
        Id = scene.Id;
        IsScene = true;
        var firstName = first?.Name ?? "Персонаж A";
        var secondName = second?.Name ?? "Персонаж B";
        Title = $"{firstName}, {secondName}";
        SecondaryTitle = string.IsNullOrWhiteSpace(scene.Name) ? "Сцена" : scene.Name;
        var last = scene.Messages?.OrderByDescending(message => message.CreatedAt).ThenByDescending(message => message.SequenceNumber).FirstOrDefault();
        Preview = NormalizePreview(string.IsNullOrWhiteSpace(last?.Content) ? scene.Scenario : last!.Content);
        UpdatedAt = last?.CreatedAt ?? scene.CreatedAt;
        IsPinned = scene.IsPinned;
        AvatarAPath = first?.AvatarPath ?? "";
        AvatarBPath = second?.AvatarPath ?? "";
        AvatarAInitials = Initials(firstName);
        AvatarBInitials = Initials(secondName);
    }

    private static string NormalizePreview(string? text) => string.Join(" ", (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public Guid Id { get; }
    public bool IsScene { get; }
    public bool IsPinned { get; }
    public ChatListItemViewModel? ChatItem { get; }
    public SoulScene? Scene { get; }
    public string Title { get; }
    public string SecondaryTitle { get; }
    public string Preview { get; }
    public DateTimeOffset UpdatedAt { get; }
    public string AvatarAPath { get; }
    public string AvatarBPath { get; }
    public string AvatarAInitials { get; }
    public string AvatarBInitials { get; }
    public bool HasSecondAvatar => IsScene;
    public string PinIcon => IsPinned ? "📌" : "";
    public string PinMenuText => IsPinned ? "📌  Открепить" : "📌  Закрепить";
    public bool HasTypeBadge => false;
    public string TypeBadge => "";
    public string TimeLabel => UpdatedAt == DateTimeOffset.MinValue ? "" : UpdatedAt.LocalDateTime.ToString("HH:mm");
    public static ConversationListItemViewModel FromChat(SoulCharacter character, SoulChat chat) => new(new ChatListItemViewModel(character, chat));
    public static ConversationListItemViewModel FromScene(SoulScene scene, SoulCharacter? first, SoulCharacter? second) => new(scene, first, second);

    public bool MatchesSearch(string query) =>
        Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || SecondaryTitle.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Preview.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private static string Initials(string value) => string.Concat((value ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(token => char.ToUpperInvariant(token[0])));
}
