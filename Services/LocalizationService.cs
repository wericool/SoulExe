namespace SoulExe.Services;

/// <summary>WPF-free core of UI localization: string table + current language.
/// Used by ViewModels (including ConversationChecks, which has no WPF).
/// The XAML resource loading itself lives in LocalizationResourceLoader.</summary>
public static class LocalizationService
{
    public const string DefaultLanguage = "ru";
    private static readonly Dictionary<string, string> Strings = new(StringComparer.Ordinal);

    public static event EventHandler? LanguageChanged;

    public static string Current { get; private set; } = DefaultLanguage;

    public static string Normalize(string? language) =>
        string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : DefaultLanguage;

    /// <summary>Replaces the active string table and raises LanguageChanged.</summary>
    public static void ReplaceStrings(string language, IEnumerable<KeyValuePair<string, string>> entries)
    {
        Strings.Clear();
        foreach (var entry in entries)
            if (entry.Value.Length > 0)
                Strings[entry.Key] = entry.Value;
        Current = Normalize(language);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Returns the localized string for <paramref name="key"/>, or <paramref name="fallback"/> when missing.
    /// Fallbacks keep the app fully functional before the first load and for unknown keys.</summary>
    public static string Tr(string key, string fallback) =>
        Strings.TryGetValue(key, out var value) ? value : fallback;
}
