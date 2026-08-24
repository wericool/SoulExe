using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

/// <summary>Formats the cognitive-architecture status line on the character card.</summary>
public static class CognitiveStatusText
{
    public static string For(SoulCharacter? character, bool architectureEnabled)
    {
        if (character is null) return "Выберите персонажа.";
        if (!architectureEnabled)
            return "Cognitive Architecture полностью отключена для этого персонажа; его память и summary не попадут в prompt.";
        var memory = character.SoulMemoryEnabled
            ? $"{SoulMemoryPresetMode.From(character.SoulMemoryPreset).DisplayName}, каждые {character.SoulMemoryIntervalMessages} реплик диалога"
            : "выключена";
        var summary = character.AutoSummaryEnabled
            ? $"каждые {character.AutoSummaryIntervalMessages} реплик диалога"
            : "выключено";
        return $"Soul Memory: {memory}; Auto-Summary: {summary}.";
    }
}
