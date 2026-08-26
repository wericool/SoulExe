using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{

    public ObservableCollection<SoulCharacter> Characters { get; }
    public ObservableCollection<HomeCharacterCardViewModel> HomeCards { get; }
    public ObservableCollection<ChatCharacterSortOption> HomeCharacterSortOptions { get; }
    public ObservableCollection<ChatListItemViewModel> ChatListItems { get; }
    public ObservableCollection<ConversationListItemViewModel> ConversationItems { get; }
    public ObservableCollection<ChatCharacterSortOption> ChatCharacterSortOptions { get; }
    public ObservableCollection<ChatMessageViewModel> Messages { get; }
    public ObservableCollection<ChatMessageSearchResult> ChatMessageSearchResults { get; }
    public ObservableCollection<ChatMessageSearchResult> SceneMessageSearchResults { get; }
    public ObservableCollection<SceneMessageViewModel> SceneMessages { get; }
    public bool HasOlderChatMessages => _personalMessageWindowStart > 0;
    public bool HasOlderSceneMessages => _sceneMessageWindowStart > 0;
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
    public PendingDeletionRequest? PendingDeletion
    {
        get => _pendingDeletion;
        private set
        {
            if (!Set(ref _pendingDeletion, value)) return;
            OnPropertyChanged(nameof(IsPendingDeletionDialogOpen));
            ConfirmPendingDeletionCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsPendingDeletionDialogOpen => PendingDeletion is not null;
    public string? GatewayError { get => _gatewayError; private set { if (Set(ref _gatewayError, value)) OnPropertyChanged(nameof(HasGatewayError)); } }
    public bool HasGatewayError => !string.IsNullOrWhiteSpace(GatewayError);
    public string? ModelsCatalogError { get => _modelsCatalogError; private set { if (Set(ref _modelsCatalogError, value)) OnPropertyChanged(nameof(HasModelsCatalogError)); } }
    public bool HasModelsCatalogError => !string.IsNullOrWhiteSpace(ModelsCatalogError);
    public bool HasPersonas => Personas.Count > 0;

    public PersonalConversationEditorViewModel? SelectedPersonalConversation
    {
        get => _selectedPersonalConversation;
        private set => Set(ref _selectedPersonalConversation, value);
    }

    public GroupConversationEditorViewModel? SelectedGroupConversation
    {
        get => _selectedGroupConversation;
        private set => Set(ref _selectedGroupConversation, value);
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
    public ObservableCollection<ComposerAuthorOption> ComposerAuthors { get; }
    public ComposerAuthorOption ComposerAuthor
    {
        get => _composerAuthor;
        set => Set(ref _composerAuthor, value ?? ComposerAuthorOption.User);
    }
    public string GroupDraft { get => _groupDraft; set { if (Set(ref _groupDraft, value)) SendGroupMessageCommand.RaiseCanExecuteChanged(); } }
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
    public bool IsSceneConversationVisible => SelectedGroupConversation is not null && !IsSceneComposerOpen;
    public bool IsSceneFinished => SelectedGroupConversation?.Status == SceneStatus.Finished;
    public string SceneStartPauseText => ScenePresentationText.StartPause(SelectedGroupConversation?.Conversation);
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
    public bool IsSceneCountdownVisible => SceneCountdownSeconds > 0 && SelectedGroupConversation?.Status == SceneStatus.Running;
    public string SceneCountdownText => ScenePresentationText.Countdown(SceneCountdownSeconds);
    public string SceneLastMessageLabel => ScenePresentationText.LastMessage(SelectedGroupConversation?.Conversation);
    public string SceneNextSpeakerName => ScenePresentationText.NextSpeakerName(SelectedGroupConversation?.Conversation, Characters);
    public bool IsSceneSelected => SelectedGroupConversation is not null;
    public SoulCharacter? SelectedSceneCharacterA => SelectedGroupConversation?.CharacterIds.ElementAtOrDefault(0) is Guid id && id != Guid.Empty ? Characters.FirstOrDefault(character => character.Id == id) : null;
    public SoulCharacter? SelectedSceneCharacterB => SelectedGroupConversation?.CharacterIds.ElementAtOrDefault(1) is Guid id && id != Guid.Empty ? Characters.FirstOrDefault(character => character.Id == id) : null;
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
    public string NewConversationConfirmText => IsNewSceneType ? "Создать групповой разговор" : "Создать чат";

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
    public ConversationListItemViewModel? RenameScene
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

    public string SelectedChatHeaderTitle => SelectedPersonalConversation?.Name ?? "Выберите диалог";
    public string SelectedCharacterPresence => IsModelRunning ? "Онлайн" : "Локальный чат";
    public string SelectedChatLastMessageLabel
    {
        get
        {
            var message = SelectedPersonalConversation?.Conversation.Messages.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.SequenceNumber).FirstOrDefault();
            return message is null
                ? "Последнее сообщение: пока нет"
                : $"Последнее сообщение · {message.CreatedAt.LocalDateTime:dd.MM.yyyy · HH:mm}";
        }
    }
    public string SelectedCharacterCreatedLabel => SelectedCharacter is null ? "—" : SelectedCharacter.CreatedAt.LocalDateTime.ToString("dd MMM yyyy");
    public int SelectedChatMessageCount => SelectedPersonalConversation?.MessageCount ?? 0;
    public string SelectedCharacterTitle => SelectedCharacter?.Title?.Trim() ?? "";
    public bool IsCharacterDescriptionExpanded => _isCharacterDescriptionExpanded;
    public bool IsCharacterPersonalityExpanded => _isCharacterPersonalityExpanded;
    public bool IsCharacterScenarioExpanded => _isCharacterScenarioExpanded;
    public string SelectedCharacterDescriptionDisplay => CharacterCardTextDisplay.Format(SelectedCharacter?.Description, IsCharacterDescriptionExpanded);
    public string SelectedCharacterPersonalityDisplay => CharacterCardTextDisplay.Format(SelectedCharacter?.Personality, IsCharacterPersonalityExpanded);
    public string SelectedCharacterScenarioDisplay => CharacterCardTextDisplay.Format(SelectedCharacter?.Scenario, IsCharacterScenarioExpanded);
    public string CharacterDescriptionToggleText => IsCharacterDescriptionExpanded ? "Скрыть" : "Читать далее";
    public string CharacterPersonalityToggleText => IsCharacterPersonalityExpanded ? "Скрыть" : "Читать далее";
    public string CharacterScenarioToggleText => IsCharacterScenarioExpanded ? "Скрыть" : "Читать далее";
    public bool HasSelectedCharacterDescriptionOverflow => CharacterCardTextDisplay.HasOverflow(SelectedCharacter?.Description);
    public bool HasSelectedCharacterPersonalityOverflow => CharacterCardTextDisplay.HasOverflow(SelectedCharacter?.Personality);
    public bool HasSelectedCharacterScenarioOverflow => CharacterCardTextDisplay.HasOverflow(SelectedCharacter?.Scenario);
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

}
