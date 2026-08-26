using System.Windows;

namespace SoulExe.Services;

/// <summary>WPF side of localization: loads compiled Localization/Strings.{lang}.xaml
/// dictionaries, feeds them into LocalizationService and swaps the merged dictionary
/// so every DynamicResource string in open views updates without restart.
/// Not included in SoulExe.ConversationChecks (no WPF there).</summary>
public static class LocalizationResourceLoader
{
    private static ResourceDictionary? _activeDictionary;

    public static void Apply(string? language)
    {
        var lang = LocalizationService.Normalize(language);
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Localization/Strings.{lang}.xaml")
        };

        LocalizationService.ReplaceStrings(
            lang,
            dictionary.Keys.Cast<object>()
                .Where(key => key is string)
                .Select(key => new KeyValuePair<string, string>((string)key, dictionary[key] as string ?? "")));

        if (Application.Current is not { } application) return;
        var merged = application.Resources.MergedDictionaries;
        if (_activeDictionary is not null) merged.Remove(_activeDictionary);
        merged.Add(dictionary);
        _activeDictionary = dictionary;
    }
}
