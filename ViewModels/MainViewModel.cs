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
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private const int MobileServerPort = 8000;
    private readonly CharacterLibraryService _library = AppServices.CharacterLibrary;
    private readonly JsonDataStore _store = AppServices.DataStore;
    private readonly LlamaServerService _llama = new();
    private readonly LlamaInstallerService _installer = new();
    private readonly ModelsHubService _modelsHub = AppServices.ModelsHub;
    private readonly RecommendedModelsService _recommendedModels = AppServices.RecommendedModels;
    private readonly LorebookService _lorebooks = AppServices.Lorebooks;
    private readonly PersonaService _personas = AppServices.Personas;
    private readonly StateVariableService _stateVariables = AppServices.StateVariables;
    private readonly CharacterCardImportService _characterCards = AppServices.CharacterCards;
    private readonly CharactersGatewayService _charactersGateway = AppServices.CharactersGateway;
    private readonly SoulOfWaifuImportService _soulOfWaifuImporter = AppServices.SoulOfWaifuImporter;
    private readonly CharacterCardExportService _characterCardExporter = AppServices.CharacterCardExporter;
    private readonly ConversationService _conversations = AppServices.Conversations;
    private readonly ConversationTurnRunner _conversationTurnRunner = new(AppServices.ConversationPrompt, AppServices.DataStore);
    private readonly SceneTurnScheduler _sceneTurnScheduler = new(AppServices.Conversations);
    private readonly NetworkChatServer _network;
    private readonly CognitiveBackgroundScheduler _cognitiveScheduler;
    private readonly MemoryDiagnosticsSampler _memoryDiagnostics;
    private SoulCharacter? _selectedCharacter;
    private PersonalConversationEditorViewModel? _selectedPersonalConversation;
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
    private PendingDeletionRequest? _pendingDeletion;
    private bool _isBusy;
    private string _gatewayQuery = "";
    private GatewayAssetItem? _selectedGatewayAsset;
    private string _gatewayCategory = "soul";
    private bool _gatewayNsfwEnabled;
    private int _gatewayPage = 1;
    private bool _gatewayHasMore = true;
    private string? _gatewayError;
    private string? _modelsCatalogError;
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
    private IReadOnlyList<ConversationSnapshot> _conversationSnapshots = [];
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
    private ConversationListItemViewModel? _renameScene;
    private string _renameSceneNameDraft = "";
    private bool _isMessageActionMenuOpen;
    private ChatMessageViewModel? _messageActionMenuItem;
    private bool _isChatMessageSearchOpen;
    private string _chatMessageSearchQuery = "";
    private ChatMessageSearchResult? _selectedChatMessageSearchResult;
    private bool _isSceneMessageSearchOpen;
    private string _sceneMessageSearchQuery = "";
    private ChatMessageSearchResult? _selectedSceneMessageSearchResult;
    private int _personalMessageWindowStart;
    private int _sceneMessageWindowStart;
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
    private string _cognitiveBackgroundMode = BackgroundModes.Idle;
    private int _cognitiveBackgroundIdleSeconds = 60;
    private string _cognitiveBackgroundStatus = "Фоновые обновления памяти готовы.";
    private string _mobileAccessUsername = "admin";
    private string _mobileAccessPassword = "admin";
    private string _mobileAccessPasswordHash = "";
    private bool _startMobileServerOnLaunch;
    // API may already be running before this ViewModel owns a Process instance.
    // Keep UI state in sync with successful health checks as well as owned processes.
    private volatile bool _isModelApiAvailable;
    private string _characterGenerationIdea = "";
    private bool _isCharacterGeneratorOpen;
    private bool _isCharacterCreationDialogOpen;
    private string _characterCreationMode = "";
    private string _characterNameDraft = "";
    private SoulCharacter? _characterPendingDeletion;
    private string _characterEditorTab = "info";
    private GroupConversationEditorViewModel? _selectedGroupConversation;
    private SoulCharacter? _sceneCharacterA;
    private SoulCharacter? _sceneCharacterB;
    private string _sceneNameDraft = SceneDraftDefaults.Name;
    private string _sceneScenarioDraft = SceneDraftDefaults.Scenario;
    private string _sceneLocationDraft = SceneDraftDefaults.Location;
    private string _sceneTimeDraft = SceneDraftDefaults.Time;
    private string _sceneMoodDraft = SceneDraftDefaults.Mood;
    private string _sceneGoalDraft = SceneDraftDefaults.Goal;
    private string _sceneRelationshipDraft = "";
    private string _sceneTurnModeDraft = "alternate";
    private int _sceneDelaySecondsDraft = 10;
    private bool _sceneEnforceContractDraft = true;
    private bool _sceneAdvanceNarrativeDraft = true;
    private string _groupDraft = "";
    private ComposerAuthorOption _composerAuthor = ComposerAuthorOption.User;
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
        ComposerAuthors = new ObservableCollection<ComposerAuthorOption> { ComposerAuthorOption.User, ComposerAuthorOption.Director };
        HomeCards = new ObservableCollection<HomeCharacterCardViewModel>();
        HomeCharacterSortOptions = new ObservableCollection<ChatCharacterSortOption>(
        [
            new("recent", "По дате последней реплики"),
            new("count", "По количеству реплик"),
            new("created", "По дате создания"),
            new("name", "По алфавиту")
        ]);
        ChatListItems = new ObservableCollection<ChatListItemViewModel>();
        ConversationItems = new ObservableCollection<ConversationListItemViewModel>();
        ChatMessageSearchResults = new ObservableCollection<ChatMessageSearchResult>();
        SceneMessageSearchResults = new ObservableCollection<ChatMessageSearchResult>();
        ChatCharacterSortOptions = new ObservableCollection<ChatCharacterSortOption>(
        [
            new("recent", "По дате"),
            new("name", "По имени")
        ]);
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
        SceneMessages = new ObservableCollection<SceneMessageViewModel>();
        _network = new NetworkChatServer(AskFromNetworkAsync, () => Characters, ControlSceneFromNetworkAsync, () => (MobileAccessUsername, _mobileAccessPasswordHash), GenerateCharacterFromNetworkAsync, GeneratePersonaFromNetworkAsync, ExpandCharacterFieldFromNetworkAsync, RefreshDesktopAfterNetworkMutationAsync);
        _cognitiveScheduler = new CognitiveBackgroundScheduler(ReportCognitiveBackground);
        _memoryDiagnostics = new MemoryDiagnosticsSampler(
            () => MemoryDiagnosticsSampler.Capture(_llama.ProcessId, _cognitiveScheduler.PendingCount, _cognitiveScheduler.RunningCount, _network.SessionCount),
            message => AppLog.Write(message));
        NavigateCommand = new RelayCommand(page => NavigateTo(page as string ?? "Chat"));
        SelectLibraryTabCommand = new RelayCommand(value => LibraryTab = value as string ?? "characters");
        SetModelsHubTabCommand = new RelayCommand(tab => SetModelsHubTab(tab as string ?? "Recommendations"));
        SelectCharacterEditorTabCommand = new RelayCommand(tab => CharacterEditorTab = tab as string ?? "info");

        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null && !string.IsNullOrWhiteSpace(Draft));
        ContinueChatCommand = new AsyncRelayCommand(_ => ContinueChatAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null);
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
        DeleteCharacterCommand = new AsyncRelayCommand(_ => DeleteCharacterAsync(), _ => !IsBusy && SelectedCharacter is not null);
        OpenCharacterChatCommand = new AsyncRelayCommand(x => OpenCharacterChatAsync(x as SoulCharacter), _ => !IsBusy);
        OpenCharacterEditorCommand = new AsyncRelayCommand(x => OpenCharacterEditorAsync(x as SoulCharacter), _ => !IsBusy);
        ConfirmDeleteCharacterCommand = new AsyncRelayCommand(x => ConfirmDeleteCharacterAsync(x as SoulCharacter), _ => !IsBusy);
        ConfirmCharacterDeleteCommand = new AsyncRelayCommand(_ => ConfirmCharacterDeleteAsync(), _ => !IsBusy && CharacterPendingDeletion is not null);
        CancelCharacterDeleteCommand = new RelayCommand(_ => CharacterPendingDeletion = null);
        AddChatCommand = new AsyncRelayCommand(_ => OpenNewChatCharacterPickerAsync(), _ => !IsBusy && Characters.Count > 0);
        ConfirmNewChatForCharacterCommand = new AsyncRelayCommand(_ => CreateNewConversationAsync(), _ => !IsBusy && (IsNewSceneType ? SceneCharacterA is not null && SceneCharacterB is not null && SceneCharacterA.Id != SceneCharacterB.Id : NewChatCharacter is not null));
        CancelNewChatCharacterPickerCommand = new RelayCommand(_ => { IsNewChatCharacterPickerOpen = false; NewChatNameDraft = "Новый чат"; NewConversationType = "chat"; });
        ToggleChatPinnedCommand = new AsyncRelayCommand(x => ToggleChatPinnedAsync(x as ChatListItemViewModel), _ => !IsBusy);
        ToggleConversationPinnedCommand = new AsyncRelayCommand(x => ToggleConversationPinnedAsync(x as ConversationListItemViewModel), _ => !IsBusy);
        BeginRenameConversationCommand = new RelayCommand(x => BeginRenameConversation(x as ConversationListItemViewModel), _ => !IsBusy);
        DeleteConversationCommand = new AsyncRelayCommand(x => RequestConversationDeletionAsync(x as ConversationListItemViewModel), _ => !IsBusy);
        ConfirmPendingDeletionCommand = new AsyncRelayCommand(_ => ConfirmPendingDeletionAsync(), _ => !IsBusy && PendingDeletion is not null);
        CancelPendingDeletionCommand = new RelayCommand(_ => PendingDeletion = null);
        ConfirmRenameSceneCommand = new AsyncRelayCommand(_ => ConfirmRenameConversationAsync(), _ => !IsBusy && RenameScene is not null && !string.IsNullOrWhiteSpace(RenameSceneNameDraft));
        CancelRenameSceneCommand = new RelayCommand(_ => CloseRenameSceneDialog());
        OpenChatActionMenuCommand = new RelayCommand(x => OpenChatActionMenu(x as ChatListItemViewModel));
        CloseChatActionMenuCommand = new RelayCommand(_ => CloseChatActionMenu());
        ConfirmRenameChatCommand = new AsyncRelayCommand(_ => ConfirmRenameConversationAsync(), _ => !IsBusy && RenameChatItem is not null && !string.IsNullOrWhiteSpace(RenameChatNameDraft));
        CancelRenameChatDialogCommand = new RelayCommand(_ => CloseRenameChatDialog());
        OpenMessageActionMenuCommand = new RelayCommand(x => OpenMessageActionMenu(x as ChatMessageViewModel));
        CloseMessageActionMenuCommand = new RelayCommand(_ => IsMessageActionMenuOpen = false);
        ToggleChatMessageSearchCommand = new RelayCommand(_ => IsChatMessageSearchOpen = !IsChatMessageSearchOpen, _ => SelectedPersonalConversation is not null);
        CloseChatMessageSearchCommand = new RelayCommand(_ => IsChatMessageSearchOpen = false);
        SelectChatMessageSearchResultCommand = new RelayCommand(x => SelectChatMessageSearchResult(x as ChatMessageSearchResult));
        ToggleSceneMessageSearchCommand = new RelayCommand(_ => IsSceneMessageSearchOpen = !IsSceneMessageSearchOpen, _ => SelectedGroupConversation is not null);
        CloseSceneMessageSearchCommand = new RelayCommand(_ => IsSceneMessageSearchOpen = false);
        SelectSceneMessageSearchResultCommand = new RelayCommand(x => SelectSceneMessageSearchResult(x as ChatMessageSearchResult));
        LoadOlderChatMessagesCommand = new RelayCommand(_ => LoadOlderChatMessages(), _ => HasOlderChatMessages);
        LoadOlderSceneMessagesCommand = new RelayCommand(_ => LoadOlderSceneMessages(), _ => HasOlderSceneMessages);
        ToggleCharacterCardSectionCommand = new RelayCommand(x => ToggleCharacterCardSection(x as string));
        DeleteChatCommand = new AsyncRelayCommand(_ => RequestSelectedChatDeletionAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null);
        CreateChatForCharacterCommand = new AsyncRelayCommand(x => CreateChatForCharacterAsync(x as ChatListItemViewModel), _ => !IsBusy);
        DeleteChatListItemCommand = new AsyncRelayCommand(x => RequestChatListItemDeletionAsync(x as ChatListItemViewModel), _ => !IsBusy);
        BeginRenameChatCommand = new RelayCommand(x => BeginRenameChat(x as ChatListItemViewModel), x => !IsBusy && x is ChatListItemViewModel);
        SaveRenameChatCommand = new AsyncRelayCommand(x => SaveRenameChatAsync(x as ChatListItemViewModel), x => !IsBusy && x is ChatListItemViewModel item && item.IsRenaming);
        CancelRenameChatCommand = new RelayCommand(x => CancelRenameChat(x as ChatListItemViewModel), x => x is ChatListItemViewModel item && item.IsRenaming);
        ChooseServerCommand = new RelayCommand(_ => ChooseServer());
        ChooseModelCommand = new AsyncRelayCommand(_ => ChooseModelAsync(), _ => !IsBusy);
        ChooseAvatarCommand = new RelayCommand(_ => ChooseAvatar());
        SaveCharacterCommand = new AsyncRelayCommand(_ => SaveCharacterAsync(), _ => !IsBusy && SelectedCharacter is not null);
        SaveChatStartingContextCommand = new AsyncRelayCommand(_ => SaveChatStartingContextAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null);
        ExpandCharacterFieldCommand = new AsyncRelayCommand(value => ExpandCharacterFieldAsync(value as string), _ => !IsBusy && SelectedCharacter is not null);
        PreviousVariantCommand = new AsyncRelayCommand(x => ShiftVariantAsync(x as ChatMessageViewModel, -1), x => x is ChatMessageViewModel message && message.CanMovePrevious && SelectedPersonalConversation is not null);
        NextVariantCommand = new AsyncRelayCommand(x => ShiftVariantAsync(x as ChatMessageViewModel, 1), x => x is ChatMessageViewModel message && message.CanMoveNext && SelectedPersonalConversation is not null);
        BeginEditMessageCommand = new RelayCommand(x => BeginMessageEdit(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel && SelectedPersonalConversation is not null);
        CancelEditMessageCommand = new RelayCommand(x => CancelMessageEdit(x as ChatMessageViewModel), x => x is ChatMessageViewModel message && message.IsEditing);
        SaveEditMessageCommand = new AsyncRelayCommand(x => SaveMessageEditAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel message && message.IsEditing && SelectedPersonalConversation is not null);
        DeleteMessageCommand = new AsyncRelayCommand(x => RequestMessageDeletionAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel && SelectedPersonalConversation is not null);
        ContinueFromMessageCommand = new AsyncRelayCommand(x => ContinueFromMessageAsync(x as ChatMessageViewModel), x => !IsBusy && x is ChatMessageViewModel message && message.CanContinueFromHere && SelectedPersonalConversation is not null);
        CreateSceneCommand = new AsyncRelayCommand(_ => CreateSceneAsync(), _ => !IsBusy && SceneCharacterA is not null && SceneCharacterB is not null && SceneCharacterA.Id != SceneCharacterB.Id);
        BeginCreateSceneCommand = new RelayCommand(_ => BeginCreateScene(), _ => !IsBusy);
        CancelCreateSceneCommand = new RelayCommand(_ => { IsSceneComposerOpen = false; });
        SaveSceneCommand = new AsyncRelayCommand(_ => SaveSceneAsync(), _ => !IsBusy && SelectedGroupConversation is not null);
        DeleteSceneCommand = new AsyncRelayCommand(_ => RequestSelectedSceneDeletionAsync(), _ => !IsBusy && SelectedGroupConversation is not null);
        StartSceneCommand = new AsyncRelayCommand(_ => StartSceneAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating);
        PauseSceneCommand = new AsyncRelayCommand(_ => PauseSceneAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating);
        ToggleSceneStartPauseCommand = new AsyncRelayCommand(_ => ToggleSceneStartPauseAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating);
        NextSceneTurnCommand = new AsyncRelayCommand(_ => GenerateNextSceneTurnAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating);
        ChooseSceneSpeakerCommand = new AsyncRelayCommand(value => ChooseSceneSpeakerAsync(value as SoulCharacter), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating);
        SendGroupMessageCommand = new AsyncRelayCommand(_ => SendGroupMessageAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !string.IsNullOrWhiteSpace(GroupDraft));
        FinishSceneCommand = new AsyncRelayCommand(_ => FinishSceneAsync(), _ => !IsBusy && SelectedGroupConversation is not null && !IsSceneGenerating && SelectedGroupConversation.Status != SceneStatus.Finished);
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
        SaveMobileAccessCommand = new AsyncRelayCommand(_ => SaveMobileAccessAsync(), _ => !IsBusy);
        ResetChatAppearanceCommand = new RelayCommand(_ => ResetChatAppearance());
        SelectOptionsTabCommand = new RelayCommand(value => SelectOptionsTab(value as string));
        SetChatAppearanceColorCommand = new RelayCommand(value => SetChatAppearanceColor(value as string));
        AddLorebookCommand = new AsyncRelayCommand(_ => AddLorebookAsync(), _ => !IsBusy);
        OpenLibraryLoreEditorCommand = new RelayCommand(value => OpenLibraryLoreEditor(value as SoulLorebook));
        CloseLibraryLoreEditorCommand = new RelayCommand(_ => IsLibraryLoreEditorOpen = false);
        DeleteLoreEntryCommand = new AsyncRelayCommand(value => RequestLoreEntryDeletionAsync(value as SoulLoreEntry), _ => !IsBusy && SelectedLorebook is not null);
        DeleteLorebookCommand = new AsyncRelayCommand(value => RequestLorebookDeletionAsync(value as SoulLorebook), _ => !IsBusy);
        SaveLorebookCommand = new AsyncRelayCommand(_ => SaveLorebookAsync(), _ => !IsBusy && SelectedLorebook is not null);
        AddLoreEntryCommand = new AsyncRelayCommand(_ => AddLoreEntryAsync(), _ => !IsBusy && SelectedLorebook is not null);
        AddPersonaCommand = new AsyncRelayCommand(_ => AddPersonaAsync(), _ => !IsBusy);
        GeneratePersonaDescriptionCommand = new AsyncRelayCommand(_ => GeneratePersonaDescriptionAsync(), _ => !IsBusy && SelectedPersona is not null);
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
        UpdateMemoryCommand = new AsyncRelayCommand(_ => UpdateCurrentMemoryAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null);
        UpdateSummaryCommand = new AsyncRelayCommand(_ => UpdateCurrentSummaryAsync(), _ => !IsBusy && SelectedCharacter is not null && SelectedPersonalConversation is not null);
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
        viewModel._memoryDiagnostics.Start();
        return viewModel;
    }

    private async Task LoadAsync()
    {
        var data = await _store.ReadAsync(root => root);
        if (data.Preferences.CognitiveMaintenancePolicyVersion < 3)
        {
            await _store.MutateAsync(root =>
            {
                // Earlier releases could begin a multi-call Full Soul Memory pipeline immediately
                // after an answer. Persist all pending messages, but consume them only after a
                // real reading pause so a new chat turn always has priority over maintenance.
                if (string.Equals(root.Preferences.CognitiveBackgroundMode, BackgroundModes.Immediate, StringComparison.OrdinalIgnoreCase))
                    root.Preferences.CognitiveBackgroundMode = BackgroundModes.Idle;
                root.Preferences.CognitiveBackgroundIdleSeconds = Math.Max(60, root.Preferences.CognitiveBackgroundIdleSeconds);
                root.Preferences.CognitiveMaintenancePolicyVersion = 3;
            }, "migrate_cognitive_maintenance_v3");
            data = await _store.ReadAsync(root => root);
        }
        if (!string.IsNullOrEmpty(data.Preferences.MobileAccessPassword))
        {
            await _store.MutateAsync(root =>
            {
                root.Preferences.MobileAccessPassword = "";
                root.Preferences.LocalWebServerEnabled = false;
            }, "remove_legacy_mobile_password");
            data = await _store.ReadAsync(root => root);
            Status = "Для мобильного доступа задайте новый пароль; автозапуск сервера временно отключён.";
        }
        ServerPath = data.Preferences.LlamaServerPath;
        ModelPath = data.Preferences.ModelPath;
        ModelRepository = data.Preferences.ModelHuggingFaceRepository;
        _mobileAccessUsername = string.IsNullOrWhiteSpace(data.Preferences.MobileAccessUsername) ? "admin" : data.Preferences.MobileAccessUsername;
        _mobileAccessPassword = "";
        _mobileAccessPasswordHash = data.Preferences.MobileAccessPasswordHash;
        _startMobileServerOnLaunch = data.Preferences.LocalWebServerEnabled;
        OnPropertyChanged(nameof(MobileAccessUsername));
        OnPropertyChanged(nameof(MobileAccessPassword));
        OnPropertyChanged(nameof(StartMobileServerOnLaunch));
        _cognitiveSoulMemoryEnabled = data.Preferences.CognitiveSoulMemoryEnabled;
        _selectedSoulMemoryPreset = SoulMemoryPresetMode.From(data.Preferences.SoulMemoryPreset).Id;
        _cognitiveMemoryIntervalMessages = Math.Clamp(data.Preferences.CognitiveMemoryIntervalMessages, 1, 50);
        _cognitiveAutoSummaryEnabled = data.Preferences.CognitiveAutoSummaryEnabled;
        _cognitiveSummaryIntervalMessages = Math.Clamp(data.Preferences.CognitiveSummaryIntervalMessages, 1, 100);
        _cognitiveBackgroundMode = BackgroundModes.Idle;
        _cognitiveBackgroundIdleSeconds = Math.Clamp(data.Preferences.CognitiveBackgroundIdleSeconds, 60, 300);
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
        InitializeLocalization(data.Preferences.Language);
        LoadLlamaOptions(data.Preferences);
        ChatAppearance = (data.Preferences.ChatAppearance ?? new ChatAppearanceSettings()).Clone();
        LoadPromptPresetOptions(data.PromptPresets);
        NormalizeDiscreteGenerationLimits();
        var existingSetup = _installer.IsBackendInstalled(data.Preferences.ActiveBackend) && !string.IsNullOrWhiteSpace(data.Preferences.ModelPath) && File.Exists(data.Preferences.ModelPath);
        // Quick Start is a first-run flow, not a “model is missing” flow. A
        // preconfigured portable install must still show it once; after it has
        // been completed, regular launches go straight to the chat workspace.
        var needsInitialSetup = !data.Preferences.InitialSetupCompleted;
        IsInitialSetupVisible = needsInitialSetup;
        InitialSetupStep = 1;
        CurrentPage = needsInitialSetup ? "Home" : "Chat";
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
        foreach (var option in PromptPresetList.Build(presets))
            PromptPresetOptions.Add(option);
        OnPropertyChanged(nameof(SelectedPromptPresetDescription));
    }
    private async IAsyncEnumerable<string> GenerateAsync(SoulCharacter character, Guid conversationId, string text, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token, bool isContinuation = false)
    {
        var settings = await BuildLlamaSettingsAsync();
        var generationId = Guid.NewGuid().ToString("N")[..12];
        await foreach (var chunk in _conversationTurnRunner.StreamPersonalTurnAsync(
            character.Id,
            conversationId,
            text,
            isContinuation,
            settings.ContextSize,
            settings.MaxTokens,
            (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, generationId),
            token).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private IAsyncEnumerable<string> GenerateWithPromptPolicyAsync(AppSettings settings, IReadOnlyList<LlamaMessage> messages, CancellationToken token, string? diagnosticId = null) =>
        _llama.GenerateFromMessagesAsync(
            settings,
            settings.ReasoningMode ? messages : PromptRules.WithDirectResponseMode(messages),
            token,
            diagnosticId);
    private void SelectOptionsTab(string? tab)
    {
        _optionsTab = AppNavigation.NormalizeOptionsTab(tab);
        OnPropertyChanged(nameof(IsLlmOptionsTab));
        OnPropertyChanged(nameof(IsAppearanceOptionsTab));
        OnPropertyChanged(nameof(IsMobileOptionsTab));
        OnPropertyChanged(nameof(IsModelsOptionsTab));
        OnPropertyChanged(nameof(IsSetupOptionsTab));
    }
    private void ChooseServer()
    {
        var dialog = new OpenFileDialog { Filter = "llama-server.exe|llama-server.exe|Executable files|*.exe" };
        if (dialog.ShowDialog() == true) ServerPath = dialog.FileName;
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
    private void HandleError(string context, Exception exception)
    {
        AppLog.Write(context, exception);
        Status = ContextCapacity.IsOverflow(exception)
            ? ContextCapacity.FormatUserMessage(context)
            : $"{context}: {exception.Message}";
    }
    private static bool IsContextCapacityError(Exception exception) => ContextCapacity.IsOverflow(exception);
    private void RaiseAllCommands()
    {
        foreach (var command in AllCommands())
            command.RaiseCanExecuteChanged();
    }

    private IEnumerable<IRaiseCanExecute> AllCommands()
    {
        yield return SendCommand;
        yield return ContinueChatCommand;
        yield return StartModelCommand;
        yield return StopModelCommand;
        yield return ToggleModelStartStopCommand;
        yield return InstallEngineCommand;
        yield return PauseModelDownloadCommand;
        yield return ResumeModelDownloadCommand;
        yield return ToggleModelDownloadCommand;
        yield return CancelModelDownloadCommand;
        yield return UseStarterModelCommand;
        yield return ToggleNetworkCommand;
        yield return ChooseModelCommand;
        yield return SetupInstallEngineCommand;
        yield return SetupSelectAndInstallBackendCommand;
        yield return SetupDownloadRecommendedCommand;
        yield return SkipInitialSetupCommand;
        yield return NextInitialSetupStepCommand;
        yield return SetupStartChatCommand;
        yield return AddCharacterCommand;
        yield return ToggleCharacterGeneratorCommand;
        yield return OpenCharacterCreationDialogCommand;
        yield return SelectCharacterCreationModeCommand;
        yield return CreateCharacterWithNameCommand;
        yield return GenerateCharacterFromIdeaCommand;
        yield return ImportCharacterCommand;
        yield return ImportSoulOfWaifuCommand;
        yield return ExportCharacterCommand;
        yield return DeleteCharacterCommand;
        yield return CreateSceneCommand;
        yield return SaveSceneCommand;
        yield return DeleteSceneCommand;
        yield return StartSceneCommand;
        yield return PauseSceneCommand;
        yield return ToggleSceneStartPauseCommand;
        yield return NextSceneTurnCommand;
        yield return ChooseSceneSpeakerCommand;
        yield return SendGroupMessageCommand;
        yield return FinishSceneCommand;
        yield return OpenCharacterChatCommand;
        yield return OpenCharacterEditorCommand;
        yield return ConfirmDeleteCharacterCommand;
        yield return ConfirmCharacterDeleteCommand;
        yield return AddChatCommand;
        yield return ConfirmNewChatForCharacterCommand;
        yield return ToggleChatPinnedCommand;
        yield return ToggleConversationPinnedCommand;
        yield return BeginRenameConversationCommand;
        yield return DeleteConversationCommand;
        yield return ConfirmRenameSceneCommand;
        yield return ToggleChatMessageSearchCommand;
        yield return ToggleSceneMessageSearchCommand;
        yield return DeleteChatCommand;
        yield return SaveCharacterCommand;
        yield return ExpandCharacterFieldCommand;
        yield return PreviousVariantCommand;
        yield return NextVariantCommand;
        yield return BeginEditMessageCommand;
        yield return CancelEditMessageCommand;
        yield return SaveEditMessageCommand;
        yield return DeleteMessageCommand;
        yield return ContinueFromMessageCommand;
        yield return SearchModelsCommand;
        yield return DownloadSelectedModelCommand;
        yield return RefreshInstalledModelsCommand;
        yield return UseInstalledModelCommand;
        yield return SaveModelSettingsCommand;
        yield return SaveChatAppearanceCommand;
        yield return AddLorebookCommand;
        yield return DeleteLoreEntryCommand;
        yield return SaveLorebookCommand;
        yield return AddLoreEntryCommand;
        yield return AddPersonaCommand;
        yield return GeneratePersonaDescriptionCommand;
        yield return OpenPersonaEditorCommand;
        yield return SavePersonaCommand;
        yield return ConfirmDeletePersonaCommand;
        yield return DeletePersonaCommand;
        yield return ChoosePersonaAvatarCommand;
        yield return LoadGatewayTrendingCommand;
        yield return SearchGatewayCommand;
        yield return ImportGatewayAssetCommand;
        yield return LoadMoreGatewayCommand;
        yield return UpdateMemoryCommand;
        yield return UpdateSummaryCommand;
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
        try
        {
            await _sceneTurnScheduler.DisposeAsync();
            await _cognitiveScheduler.DisposeAsync();
            await _network.DisposeAsync();
            await _memoryDiagnostics.DisposeAsync();
            await _llama.DisposeAsync();
        }
        finally
        {
            _installer.Dispose();
        }
    }
}
