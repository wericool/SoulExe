using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Builds the prompt-preset dropdown including the "no preset" sentinel.</summary>
public static class PromptPresetList
{
    public static readonly PromptPresetOption None = new(
        null,
        "Без пресета",
        "Используется только ваша карточка персонажа, системный промпт, лорбук, Summary и Soul Memory. Удобно, если вы хотите полностью собственные правила.",
        false);

    public static IReadOnlyList<PromptPresetOption> Build(IEnumerable<SoulPromptPreset>? presets)
    {
        var list = new List<PromptPresetOption> { None };
        foreach (var preset in presets ?? [])
        {
            list.Add(new PromptPresetOption(
                preset.Id,
                preset.Name,
                string.IsNullOrWhiteSpace(preset.Description) ? "Пользовательский системный пресет." : preset.Description,
                preset.IsBuiltIn));
        }
        return list;
    }
}
