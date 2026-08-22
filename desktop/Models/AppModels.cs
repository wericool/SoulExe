using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SoulTextWpf.Models;

public sealed class AppSettings
{
    public string LlamaServerPath { get; set; } = string.Empty;
    public string ModelPath { get; set; } = string.Empty;
    public string ModelHuggingFaceRepository { get; set; } = string.Empty;
    public string PreferredHost { get; set; } = "127.0.0.1";
    public int LlamaPort { get; set; } = 8081;
    public int NetworkPort { get; set; } = 8000;
    public bool StartNetworkServer { get; set; }
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
    public string ExtraArguments { get; set; } = string.Empty;
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
    public string StopStrings { get; set; } = string.Empty;
}

public sealed class CharacterProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Новый персонаж";
    public string Description { get; set; } = "";
    public string SystemPrompt { get; set; } = "Ты доброжелательный AI-персонаж. Отвечай естественно и оставайся в образе.";
    public string AvatarPath { get; set; } = "";
    public List<ChatMessageRecord> History { get; set; } = [];

    [JsonIgnore]
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => x[0])).ToUpperInvariant();
}

public sealed class ChatMessageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
}

public enum ChatRole
{
    User,
    Assistant,
    System
}

public sealed class AppData
{
    public AppSettings Settings { get; set; } = new();
    public List<CharacterProfile> Characters { get; set; } = [];
}

public sealed record LlamaMessage(string role, string content);
public sealed record NetworkChatRequest(string CharacterId, string ChatId, string Message);
