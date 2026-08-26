using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using SoulExe.Models;

namespace SoulExe.ViewModels;

public sealed class LlamaRuntimeOptions : INotifyPropertyChanged
{
    private int _contextSize = 8192, _maxTokens = 1024, _topK = 40, _gpuLayers, _cpuThreads, _cpuMoeLayers, _batchSize = 512, _parallelSlots = 1, _reasoningBudget = -1, _dryAllowedLength = 2, _llamaPort = 8081;
    private double _temperature = 0.8, _topP = 0.95, _repeatPenalty = 1.05, _frequencyPenalty, _presencePenalty, _minP, _dynamicTemperatureMin, _dynamicTemperatureMax, _dynamicTemperatureExponent = 1d, _xtcProbability, _xtcThreshold, _dryMultiplier, _dryBase = 1.75;
    private bool _flashAttention, _useMlock, _useMmap = true, _reasoningMode = true, _enableAdvancedSampling;
    private string _kvCacheType = "f16", _chatTemplate = "auto", _stopStrings = "", _extraArguments = "", _engineBackend = "cpu";

    public int ContextSize { get => _contextSize; set => Set(ref _contextSize, Math.Max(1024, value)); }
    public int MaxTokens { get => _maxTokens; set => Set(ref _maxTokens, Math.Max(1, value)); }
    public int TopK { get => _topK; set => Set(ref _topK, Math.Max(0, value)); }
    public int GpuLayers { get => _gpuLayers; set => Set(ref _gpuLayers, Math.Max(0, value)); }
    public int CpuThreads { get => _cpuThreads; set => Set(ref _cpuThreads, Math.Max(0, value)); }
    public int CpuMoeLayers { get => _cpuMoeLayers; set => Set(ref _cpuMoeLayers, Math.Max(0, value)); }
    public int BatchSize { get => _batchSize; set => Set(ref _batchSize, Math.Max(0, value)); }
    public int ParallelSlots { get => _parallelSlots; set => Set(ref _parallelSlots, Math.Clamp(value, 1, 16)); }
    public int ReasoningBudget { get => _reasoningBudget; set => Set(ref _reasoningBudget, value); }
    public int DryAllowedLength { get => _dryAllowedLength; set => Set(ref _dryAllowedLength, Math.Max(0, value)); }
    public int LlamaPort { get => _llamaPort; set => Set(ref _llamaPort, Math.Clamp(value, 1, 65535)); }
    public double Temperature { get => _temperature; set => Set(ref _temperature, Math.Clamp(value, 0, 2)); }
    public double TopP { get => _topP; set => Set(ref _topP, Math.Clamp(value, 0, 1)); }
    public double RepeatPenalty { get => _repeatPenalty; set => Set(ref _repeatPenalty, Math.Clamp(value, 0, 3)); }
    public double FrequencyPenalty { get => _frequencyPenalty; set => Set(ref _frequencyPenalty, Math.Clamp(value, -2, 2)); }
    public double PresencePenalty { get => _presencePenalty; set => Set(ref _presencePenalty, Math.Clamp(value, -2, 2)); }
    public double MinP { get => _minP; set => Set(ref _minP, Math.Clamp(value, 0, 1)); }
    public double DynamicTemperatureMin { get => _dynamicTemperatureMin; set => Set(ref _dynamicTemperatureMin, Math.Max(0, value)); }
    public double DynamicTemperatureMax { get => _dynamicTemperatureMax; set => Set(ref _dynamicTemperatureMax, Math.Max(0, value)); }
    public double DynamicTemperatureExponent { get => _dynamicTemperatureExponent; set => Set(ref _dynamicTemperatureExponent, Math.Clamp(value, 0.1d, 5d)); }
    public double XtcProbability { get => _xtcProbability; set => Set(ref _xtcProbability, Math.Clamp(value, 0, 1)); }
    public double XtcThreshold { get => _xtcThreshold; set => Set(ref _xtcThreshold, Math.Clamp(value, 0, 1)); }
    public double DryMultiplier { get => _dryMultiplier; set => Set(ref _dryMultiplier, Math.Max(0, value)); }
    public double DryBase { get => _dryBase; set => Set(ref _dryBase, Math.Max(0, value)); }
    public bool FlashAttention { get => _flashAttention; set => Set(ref _flashAttention, value); }
    public bool UseMlock { get => _useMlock; set => Set(ref _useMlock, value); }
    public bool UseMmap { get => _useMmap; set => Set(ref _useMmap, value); }
    public bool ReasoningMode { get => _reasoningMode; set => Set(ref _reasoningMode, value); }
    public bool EnableAdvancedSampling { get => _enableAdvancedSampling; set => Set(ref _enableAdvancedSampling, value); }
    public string EngineBackend { get => _engineBackend; set => Set(ref _engineBackend, string.IsNullOrWhiteSpace(value) ? "cpu" : value); }
    public string KvCacheType { get => _kvCacheType; set => Set(ref _kvCacheType, value ?? "f16"); }
    public string ChatTemplate { get => _chatTemplate; set => Set(ref _chatTemplate, value ?? "auto"); }
    public string StopStrings { get => _stopStrings; set => Set(ref _stopStrings, value ?? ""); }
    public string ExtraArguments { get => _extraArguments; set => Set(ref _extraArguments, value ?? ""); }


    public void ApplyFromPreferences(AppPreferences p)
    {
        EngineBackend = p.ActiveBackend;
        LlamaPort = p.LlamaPort;
        ContextSize = p.ContextSize;
        MaxTokens = p.MaxTokens;
        Temperature = p.Temperature;
        TopP = p.TopP;
        TopK = p.TopK;
        RepeatPenalty = p.RepeatPenalty;
        GpuLayers = p.GpuLayers;
        FlashAttention = p.FlashAttention;
        UseMlock = p.UseMlock;
        UseMmap = p.UseMmap;
        KvCacheType = p.KvCacheType;
        CpuThreads = p.CpuThreads;
        CpuMoeLayers = p.CpuMoeLayers;
        BatchSize = p.BatchSize;
        ParallelSlots = p.ParallelSlots;
        ChatTemplate = p.ChatTemplate;
        ReasoningMode = p.ReasoningMode;
        ReasoningBudget = p.ReasoningBudget;
        FrequencyPenalty = p.FrequencyPenalty;
        PresencePenalty = p.PresencePenalty;
        EnableAdvancedSampling = p.EnableAdvancedSampling;
        MinP = p.MinP;
        DynamicTemperatureMin = p.DynamicTemperatureMin;
        DynamicTemperatureMax = p.DynamicTemperatureMax;
        DynamicTemperatureExponent = p.DynamicTemperatureExponent;
        XtcProbability = p.XtcProbability;
        XtcThreshold = p.XtcThreshold;
        DryMultiplier = p.DryMultiplier;
        DryBase = p.DryBase;
        DryAllowedLength = p.DryAllowedLength;
        StopStrings = p.StopStrings;
        ExtraArguments = p.ExtraLlamaArguments;
    }

    public void WriteToPreferences(AppPreferences p)
    {
        p.ActiveBackend = EngineBackend;
        p.LlamaPort = LlamaPort;
        p.ContextSize = ContextSize;
        p.MaxTokens = MaxTokens;
        p.Temperature = Temperature;
        p.TopP = TopP;
        p.TopK = TopK;
        p.RepeatPenalty = RepeatPenalty;
        p.GpuLayers = GpuLayers;
        p.FlashAttention = FlashAttention;
        p.UseMlock = UseMlock;
        p.UseMmap = UseMmap;
        p.KvCacheType = KvCacheType;
        p.CpuThreads = CpuThreads;
        p.CpuMoeLayers = CpuMoeLayers;
        p.BatchSize = BatchSize;
        p.ParallelSlots = ParallelSlots;
        p.ChatTemplate = ChatTemplate;
        p.ReasoningMode = ReasoningMode;
        p.ReasoningBudget = ReasoningBudget;
        p.FrequencyPenalty = FrequencyPenalty;
        p.PresencePenalty = PresencePenalty;
        p.EnableAdvancedSampling = EnableAdvancedSampling;
        p.MinP = MinP;
        p.DynamicTemperatureMin = DynamicTemperatureMin;
        p.DynamicTemperatureMax = DynamicTemperatureMax;
        p.DynamicTemperatureExponent = DynamicTemperatureExponent;
        p.XtcProbability = XtcProbability;
        p.XtcThreshold = XtcThreshold;
        p.DryMultiplier = DryMultiplier;
        p.DryBase = DryBase;
        p.DryAllowedLength = DryAllowedLength;
        p.StopStrings = StopStrings;
        p.ExtraLlamaArguments = ExtraArguments;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
