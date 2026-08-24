namespace SoulExe.ViewModels;

/// <summary>Collapsed/expanded preview text for character card fields in the chat header.</summary>
public static class CharacterCardTextDisplay
{
    public const int CollapsedLength = 50;

    public static bool HasOverflow(string? text)
    {
        var prepared = Normalize(text);
        return prepared.Length > CollapsedLength;
    }

    public static string Format(string? text, bool expanded)
    {
        var prepared = Normalize(text);
        return !expanded && prepared.Length > CollapsedLength
            ? prepared[..CollapsedLength].TrimEnd() + "…"
            : prepared;
    }

    private static string Normalize(string? text) =>
        string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
