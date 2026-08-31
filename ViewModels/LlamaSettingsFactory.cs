using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>
/// Builds <see cref="AppSettings"/> for llama generation from the live UI options.
/// Keeps discrete step snapping and mapping out of MainViewModel.
/// </summary>
public static class LlamaSettingsFactory
{
    public static readonly int[] ContextSizeSteps = [2048, 4096, 8192, 16384, 32768, 65536, 131072];
    public static readonly int[] MaxTokensSteps = [128, 256, 512, 768, 1024, 1536, 2048, 3072, 4096, 8192, 16384, 32768, 65536];

    public static int SnapToStep(int value, IReadOnlyList<int> steps) =>
        steps.OrderBy(step => Math.Abs(step - value)).ThenBy(step => step).First();

    public static void NormalizeDiscreteLimits(LlamaRuntimeOptions options)
    {
        options.ContextSize = SnapToStep(options.ContextSize, ContextSizeSteps);
        options.MaxTokens = SnapToStep(options.MaxTokens, MaxTokensSteps);
    }

    public static AppSettings Build(
        LlamaRuntimeOptions options,
        string serverPath,
        string modelPath,
        string modelRepository,
        int networkPort)
    {
        return new AppSettings
        {
            LlamaServerPath = serverPath,
            ModelPath = modelPath,
            ModelHuggingFaceRepository = modelRepository,
            PreferredHost = "127.0.0.1",
            LlamaPort = options.LlamaPort,
            NetworkPort = networkPort,
            ContextSize = options.ContextSize,
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            TopP = options.TopP,
            TopK = options.TopK,
            RepeatPenalty = options.RepeatPenalty,
            GpuLayers = options.GpuLayers,
            FlashAttention = options.FlashAttention,
            UseMlock = options.UseMlock,
            UseMmap = options.UseMmap,
            KvCacheType = options.KvCacheType,
            CpuThreads = options.CpuThreads,
            CpuMoeLayers = options.CpuMoeLayers,
            BatchSize = options.BatchSize,
            ParallelSlots = options.ParallelSlots,
            ChatTemplate = options.ChatTemplate,
            ReasoningMode = options.ReasoningMode,
            ReasoningBudget = options.ReasoningBudget,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            EnableAdvancedSampling = options.EnableAdvancedSampling,
            MinP = options.MinP,
            DynamicTemperatureMin = options.DynamicTemperatureMin,
            DynamicTemperatureMax = options.DynamicTemperatureMax,
            DynamicTemperatureExponent = options.DynamicTemperatureExponent,
            XtcProbability = options.XtcProbability,
            XtcThreshold = options.XtcThreshold,
            DryMultiplier = options.DryMultiplier,
            DryBase = options.DryBase,
            DryAllowedLength = options.DryAllowedLength,
            StopStrings = options.StopStrings,
            ExtraArguments = options.ExtraArguments
        };
    }

    /// <summary>Copy used for background summary/memory: lower temperature and capped tokens.</summary>
    public static AppSettings ForCognitiveMaintenance(AppSettings source)
    {
        var settings = Clone(source);
        // The combined pass can return summary + the enabled memory fields in one response.
        // A slightly larger ceiling is still much faster than several separate generations.
        settings.MaxTokens = Math.Clamp(settings.MaxTokens, 768, 1280);
        settings.Temperature = Math.Min(settings.Temperature, 0.3d);
        return settings;
    }

    /// <summary>Tighter budget for two-character scene summaries.</summary>
    public static AppSettings ForSceneSummary(AppSettings source)
    {
        var settings = Clone(source);
        settings.MaxTokens = Math.Clamp(Math.Min(settings.MaxTokens, 220), 128, 220);
        settings.Temperature = Math.Min(settings.Temperature, 0.3d);
        return settings;
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        LlamaServerPath = s.LlamaServerPath,
        ModelPath = s.ModelPath,
        ModelHuggingFaceRepository = s.ModelHuggingFaceRepository,
        PreferredHost = s.PreferredHost,
        LlamaPort = s.LlamaPort,
        NetworkPort = s.NetworkPort,
        ContextSize = s.ContextSize,
        MaxTokens = s.MaxTokens,
        Temperature = s.Temperature,
        TopP = s.TopP,
        TopK = s.TopK,
        RepeatPenalty = s.RepeatPenalty,
        GpuLayers = s.GpuLayers,
        FlashAttention = s.FlashAttention,
        UseMlock = s.UseMlock,
        UseMmap = s.UseMmap,
        KvCacheType = s.KvCacheType,
        CpuThreads = s.CpuThreads,
        CpuMoeLayers = s.CpuMoeLayers,
        BatchSize = s.BatchSize,
        ParallelSlots = s.ParallelSlots,
        ChatTemplate = s.ChatTemplate,
        ReasoningMode = s.ReasoningMode,
        ReasoningBudget = s.ReasoningBudget,
        FrequencyPenalty = s.FrequencyPenalty,
        PresencePenalty = s.PresencePenalty,
        EnableAdvancedSampling = s.EnableAdvancedSampling,
        MinP = s.MinP,
        DynamicTemperatureMin = s.DynamicTemperatureMin,
        DynamicTemperatureMax = s.DynamicTemperatureMax,
        DynamicTemperatureExponent = s.DynamicTemperatureExponent,
        XtcProbability = s.XtcProbability,
        XtcThreshold = s.XtcThreshold,
        DryMultiplier = s.DryMultiplier,
        DryBase = s.DryBase,
        DryAllowedLength = s.DryAllowedLength,
        StopStrings = s.StopStrings,
        ExtraArguments = s.ExtraArguments
    };
}
