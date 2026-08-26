using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SoulExe.Models;

public sealed class SoulDataRoot
{
    public int SchemaVersion { get; set; } = 10;
    public AppPreferences Preferences { get; set; } = new();
    public List<SoulPersona> Personas { get; set; } = [];
    public List<SoulPromptPreset> PromptPresets { get; set; } = [];
    public List<SoulCharacter> Characters { get; set; } = [];
    public List<SoulLorebook> Lorebooks { get; set; } = [];
    public List<SoulModelInstallation> Models { get; set; } = [];
    public List<SoulImportRun> ImportRuns { get; set; } = [];
    /// <summary>Canonical storage for personal and group conversations.</summary>
    public List<ConversationSnapshot> Conversations { get; set; } = [];
}


public sealed class ChatAppearanceSettings : INotifyPropertyChanged
{
    private string _textColor = "#F3F6FF";
    private string _actionColor = "#F4B860";
    private string _quoteColor = "#8ECCFF";
    private string _codeColor = "#C084FC";
    private string _assistantBubbleColor = "#1F2633";
    private string _userBubbleColor = "#2F58F5";
    private string _chatBackgroundColor = "#12151D";
    private int _fontSize = 15;
    private double _bubbleMaxWidth = 720;
    private double _bubbleCornerRadius = 16;
    private bool _formatActions = true;
    private bool _formatQuotes = true;
    private bool _formatBold = true;
    private bool _formatCode = true;

    public string TextColor { get => _textColor; set => Set(ref _textColor, value); }
    public string ActionColor { get => _actionColor; set => Set(ref _actionColor, value); }
    public string QuoteColor { get => _quoteColor; set => Set(ref _quoteColor, value); }
    public string CodeColor { get => _codeColor; set => Set(ref _codeColor, value); }
    public string AssistantBubbleColor { get => _assistantBubbleColor; set => Set(ref _assistantBubbleColor, value); }
    public string UserBubbleColor { get => _userBubbleColor; set => Set(ref _userBubbleColor, value); }
    public string ChatBackgroundColor { get => _chatBackgroundColor; set => Set(ref _chatBackgroundColor, value); }
    public int FontSize { get => _fontSize; set => Set(ref _fontSize, Math.Clamp(value, 11, 24)); }
    public double BubbleMaxWidth { get => _bubbleMaxWidth; set => Set(ref _bubbleMaxWidth, Math.Clamp(value, 360, 960)); }
    public double BubbleCornerRadius { get => _bubbleCornerRadius; set => Set(ref _bubbleCornerRadius, Math.Clamp(value, 0, 28)); }
    public bool FormatActions { get => _formatActions; set => Set(ref _formatActions, value); }
    public bool FormatQuotes { get => _formatQuotes; set => Set(ref _formatQuotes, value); }
    public bool FormatBold { get => _formatBold; set => Set(ref _formatBold, value); }
    public bool FormatCode { get => _formatCode; set => Set(ref _formatCode, value); }

    public ChatAppearanceSettings Clone() => new()
    {
        TextColor = TextColor, ActionColor = ActionColor, QuoteColor = QuoteColor, CodeColor = CodeColor,
        AssistantBubbleColor = AssistantBubbleColor, UserBubbleColor = UserBubbleColor, ChatBackgroundColor = ChatBackgroundColor,
        FontSize = FontSize, BubbleMaxWidth = BubbleMaxWidth, BubbleCornerRadius = BubbleCornerRadius,
        FormatActions = FormatActions, FormatQuotes = FormatQuotes, FormatBold = FormatBold, FormatCode = FormatCode
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AppPreferences
{
    public string Language { get; set; } = "ru";
    public ChatAppearanceSettings ChatAppearance { get; set; } = new();
    public string LlamaServerPath { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string ModelHuggingFaceRepository { get; set; } = "";
    public string ActiveModelId { get; set; } = "";
    public string ActiveBackend { get; set; } = "cpu";
    public int LlamaPort { get; set; } = 8081;
    public int NetworkPort { get; set; } = 8000;
    public string MobileAccessUsername { get; set; } = "admin";
    public string MobileAccessPassword { get; set; } = "";
    public string MobileAccessPasswordHash { get; set; } = "";
    public bool LocalWebServerEnabled { get; set; }
    public bool HideReasoningBlocks { get; set; } = true;
    public bool CognitiveSoulMemoryEnabled { get; set; } = true;
    public string SoulMemoryPreset { get; set; } = "full";
    public int CognitiveMemoryIntervalMessages { get; set; } = 4;
    public bool CognitiveAutoSummaryEnabled { get; set; } = true;
    public int CognitiveSummaryIntervalMessages { get; set; } = 5;
    /// <summary>idle = wait for a reading pause; immediate = enqueue immediately without blocking the UI.</summary>
    public string CognitiveBackgroundMode { get; set; } = BackgroundModes.Idle;
    public int CognitiveBackgroundIdleSeconds { get; set; } = 60;
    public int CognitiveMaintenancePolicyVersion { get; set; } = 2;
    public bool InitialSetupCompleted { get; set; }
    public bool GatewayNsfwEnabled { get; set; }
    public string GatewayCategory { get; set; } = "soul";
    public int ContextSize { get; set; } = 8192;
    public int MaxTokens { get; set; } = 1024;
    public double Temperature { get; set; } = 0.8;
    public double TopP { get; set; } = 0.95;
    public int TopK { get; set; } = 40;
    public double RepeatPenalty { get; set; } = 1.05;
    public int GpuLayers { get; set; }
    public bool FlashAttention { get; set; }
    public bool UseMlock { get; set; }
    public bool UseMmap { get; set; } = true;
    public string KvCacheType { get; set; } = "f16";
    public int CpuThreads { get; set; }
    public int CpuMoeLayers { get; set; }
    public int BatchSize { get; set; } = 512;
    public int ParallelSlots { get; set; } = 1;
    public string ChatTemplate { get; set; } = "auto";
    public bool ReasoningMode { get; set; } = true;
    public int ReasoningBudget { get; set; } = -1;
    public double FrequencyPenalty { get; set; }
    public double PresencePenalty { get; set; }
    public bool EnableAdvancedSampling { get; set; }
    public double MinP { get; set; }
    public double DynamicTemperatureMin { get; set; }
    public double DynamicTemperatureMax { get; set; }
    public double DynamicTemperatureExponent { get; set; } = 1d;
    public double XtcProbability { get; set; }
    public double XtcThreshold { get; set; }
    public double DryMultiplier { get; set; }
    public double DryBase { get; set; } = 1.75;
    public int DryAllowedLength { get; set; } = 2;
    public string StopStrings { get; set; } = "";
    public string ExtraLlamaArguments { get; set; } = "";
}

public sealed class SoulPersona
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Пользователь";
    public string Description { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string AvatarPath { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore]
    public string Initials => InitialsHelper.FromName(Name);
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Персона" : Name;
}

public sealed class SoulPromptPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Стандартный";
    /// <summary>Short user-facing explanation of the preset effect.</summary>
    public string Description { get; set; } = "";
    public string PromptText { get; set; } = "";
    public bool IsBuiltIn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed record PromptPresetOption(Guid? Id, string Name, string Description, bool IsBuiltIn = true);

public sealed class SoulCharacter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Новый персонаж";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Personality { get; set; } = "";
    /// <summary>How visibly traits from Personality should appear in in-character replies: vivid, natural or subtle.</summary>
    public string PersonalityExpressionLevel { get; set; } = "natural";
    public string Scenario { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    /// <summary>
    /// Preferred reply language for this character, e.g. "Русский", "English", "日本語", or "Любой язык".
    /// Empty / "Любой язык" / "Auto" means follow established dialogue language.
    /// </summary>
    public string ReplyLanguage { get; set; } = "Русский";
    /// <summary>Applies the built-in readable roleplay response layout without modifying the user's own system prompt.</summary>
    public bool UseRoleplayResponseFormatting { get; set; }
    public string CreatorNotes { get; set; } = "";
    public string ExampleDialogue { get; set; } = "";
    public string AvatarPath { get; set; } = "";
    public string SourceType { get; set; } = "local";
    public string SourceUrl { get; set; } = "";
    public bool IsFavorite { get; set; }
    public string FolderName { get; set; } = "";
    public Guid? SelectedPersonaId { get; set; }
    public Guid? SelectedPromptPresetId { get; set; }
    /// <summary>Default facts about the user copied only into newly created chats for this character.</summary>
    public string DefaultUserProfile { get; set; } = "";
    /// <summary>Default starting relationship copied only into newly created chats for this character.</summary>
    public string DefaultRelationshipContext { get; set; } = "";
    public List<SoulGreeting> Greetings { get; set; } = [];
    public List<Guid> LorebookIds { get; set; } = [];
    /// <summary>Whether Soul Memory and Auto-Summary are active for this character.</summary>
    public bool CognitiveArchitectureEnabled { get; set; }
    public bool SoulMemoryEnabled { get; set; }
    public string SoulMemoryPreset { get; set; } = "full";
    public int SoulMemoryIntervalMessages { get; set; } = 4;
    public bool AutoSummaryEnabled { get; set; }
    public int AutoSummaryIntervalMessages { get; set; } = 5;
    public List<SoulStateVariable> StateVariables { get; set; } = [];
    public Guid? CurrentChatId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    [JsonIgnore]
    public string Initials => InitialsHelper.FromName(Name);
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Персонаж" : Name;
}

public sealed class SoulGreeting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public bool IsPrimary { get; set; }
    public int Position { get; set; }
}

public sealed class SoulMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SequenceNumber { get; set; }
    public SoulMessageRole Role { get; set; }
    /// <summary>Semantic author selected when the message was sent. Old records deserialize as User.</summary>
    public SoulMessageAuthorKind AuthorKind { get; set; } = SoulMessageAuthorKind.User;
    /// <summary>Persona used for this particular message, if any. It is intentionally not the character's default persona.</summary>
    public Guid? AuthorPersonaId { get; set; }
    public string AuthorName { get; set; } = "";
    /// <summary>Display snapshot, so editing a persona avatar never rewrites history.</summary>
    public string AuthorAvatarPath { get; set; } = "";
    public Guid CurrentVariantId { get; set; }
    public List<SoulMessageVariant> Variants { get; set; } = [];
    public List<SoulAttachment> Attachments { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EditedAt { get; set; }
}

public enum SoulMessageRole { User, Assistant, System }
public enum SoulMessageAuthorKind { User, Persona, Director }

public sealed class SoulMessageVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "Основной";
    public string Content { get; set; } = "";
    public Dictionary<string, string> GenerationMetadata { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SoulAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MediaType { get; set; } = "image";
    public string LocalPath { get; set; } = "";
    public string OriginalName { get; set; } = "";
    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SoulLorebook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Новый лорбук";
    public string Description { get; set; } = "";
    /// <summary>Gateway / external id so re-import can be detected.</summary>
    public string SourceId { get; set; } = "";
    public List<SoulLoreEntry> Entries { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "Лорбук" : Name;
}

public sealed class SoulLoreEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Новая запись";
    public string Content { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public List<string> SecondaryKeywords { get; set; } = [];
    // A newly created entry should be useful immediately. Advanced keyword filtering remains available.
    public string TriggerMode { get; set; } = "always";
    public string InjectionMode { get; set; } = "passive";
    [JsonIgnore]
    public string KeywordsText
    {
        get => string.Join(", ", Keywords);
        set => Keywords = SplitKeywords(value);
    }
    [JsonIgnore]
    public string SecondaryKeywordsText
    {
        get => string.Join(", ", SecondaryKeywords);
        set => SecondaryKeywords = SplitKeywords(value);
    }
    private static List<string> SplitKeywords(string? value) => (value ?? "")
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public int InsertionOrder { get; set; }
    public int Depth { get; set; }
    public int TokenBudget { get; set; } = 512;
    public double Probability { get; set; } = 1.0;
    public bool IsEnabled { get; set; } = true;
    public Dictionary<string, string> Conditions { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SoulStateVariable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "state";
    public string DisplayName { get; set; } = "Параметр";
    public string VariableType { get; set; } = "string";
    public string DefaultValueJson { get; set; } = "\"\"";
    public string ValidationJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
}

public sealed class SoulMemoryBundle
{
    /// <summary>Last message included in a successfully completed Router cycle.</summary>
    public int LastProcessedSequence { get; set; }
    public string CharacterMemory { get; set; } = "";
    public string UserProfile { get; set; } = "";
    public string HealingLog { get; set; } = "";
    public List<SoulMemoryTopic> Topics { get; set; } = [];
    public List<SoulDiaryEntry> Diary { get; set; } = [];
    /// <summary>Rolling pre-update snapshots; kept locally so a failed or unwanted rewrite has a recoverable history.</summary>
    public List<SoulMemorySnapshot> Snapshots { get; set; } = [];
    public List<SoulMemoryAuditEntry> Audit { get; set; } = [];
    public DateTimeOffset? LastRouterUpdatedAt { get; set; }
    public DateTimeOffset? LastDiaryUpdatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class SoulMemoryTopic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "topic";
    public string Content { get; set; } = "";
    public string SourceSummary { get; set; } = "";
    public int MentionCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastRetrievedAt { get; set; }
}

public sealed class SoulDiaryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int ThroughSequence { get; set; }
    public string Content { get; set; } = "";
}

public sealed class SoulMemorySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int ThroughSequence { get; set; }
    public string CharacterMemory { get; set; } = "";
    public string UserProfile { get; set; } = "";
    public string HealingLog { get; set; } = "";
    public List<SoulMemoryTopic> Topics { get; set; } = [];
}

public sealed class SoulMemoryAuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Stage { get; set; } = "router";
    public string Status { get; set; } = "ok";
    public string Details { get; set; } = "";
    public int ThroughSequence { get; set; }
}

public sealed class SoulModelInstallation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "llm";
    public string Backend { get; set; } = "cpu";
    public string DisplayName { get; set; } = "";
    public string SourceUri { get; set; } = "";
    public string LocalPath { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long SizeBytes { get; set; }
    public string DisplaySize
    {
        get
        {
            if (SizeBytes <= 0) return "—";
            const double gb = 1024d * 1024d * 1024d;
            const double mb = 1024d * 1024d;
            if (SizeBytes >= gb) return $"{SizeBytes / gb:0.##} ГБ";
            if (SizeBytes >= mb) return $"{SizeBytes / mb:0.#} МБ";
            return $"{SizeBytes / 1024d:0.#} КБ";
        }
    }
    public Dictionary<string, string> Metadata { get; set; } = [];
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class SoulImportRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public string ReportJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
