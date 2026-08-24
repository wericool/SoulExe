using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Reads and writes global Cognitive Architecture preferences.</summary>
public static class CognitivePreferences
{
    public static void Write(
        AppPreferences preferences,
        bool memoryEnabled,
        string memoryPreset,
        int memoryInterval,
        bool summaryEnabled,
        int summaryInterval,
        string backgroundMode,
        int backgroundIdleSeconds)
    {
        preferences.CognitiveSoulMemoryEnabled = memoryEnabled;
        preferences.SoulMemoryPreset = memoryPreset;
        preferences.CognitiveMemoryIntervalMessages = memoryInterval;
        preferences.CognitiveAutoSummaryEnabled = summaryEnabled;
        preferences.CognitiveSummaryIntervalMessages = summaryInterval;
        preferences.CognitiveBackgroundMode = backgroundMode;
        preferences.CognitiveBackgroundIdleSeconds = backgroundIdleSeconds;
    }
}
