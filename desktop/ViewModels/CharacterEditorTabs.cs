namespace SoulExe.ViewModels;

/// <summary>Normalizes character-editor tab ids (info / memory / lore).</summary>
public static class CharacterEditorTabs
{
    public static string Normalize(string? tab) => tab is "memory" or "lore" ? tab : "info";
}
