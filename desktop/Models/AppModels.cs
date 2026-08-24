using System.Text.Json.Serialization;

namespace SoulExe.Models;

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

public sealed record LlamaMessage(string role, string content);
public sealed record NetworkChatRequest(string CharacterId, string ChatId, string Message, string? AuthorKind = null, string? AuthorPersonaId = null);
