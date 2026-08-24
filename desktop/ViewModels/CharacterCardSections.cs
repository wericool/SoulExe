namespace SoulExe.ViewModels;

/// <summary>Toggles which long text section is expanded on the character card.</summary>
public static class CharacterCardSections
{
    public static bool TryToggle(string? section, ref bool description, ref bool personality, ref bool scenario)
    {
        switch ((section ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "description": description = !description; return true;
            case "personality": personality = !personality; return true;
            case "scenario": scenario = !scenario; return true;
            default: return false;
        }
    }
}
