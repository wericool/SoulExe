using System.Linq;

namespace SoulExe.Services;

/// <summary>
/// Builds LANGUAGE LOCK blocks from the character's preferred reply language or, when set to
/// "any/auto", from the dominant language of the established chat/scene text.
/// </summary>
public static class ReplyLanguage
{
    public static bool LooksRussian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("ROLEPLAY PRESET — STANDARD RUSSIAN", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Always write the final roleplay reply in clear, natural Russian", StringComparison.OrdinalIgnoreCase))
            return true;

        var cyrillic = 0;
        foreach (var ch in text)
        {
            if (ch is (>= 'А' and <= 'я') or 'Ё' or 'ё') cyrillic++;
            if (cyrillic >= 4) return true;
        }
        return false;
    }

    public static bool IsAnyLanguage(string? preference)
    {
        var value = (preference ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return true;
        return value.Equals("Любой язык", StringComparison.CurrentCultureIgnoreCase)
               || value.Equals("Any", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Any language", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Auto", StringComparison.OrdinalIgnoreCase)
               || value.Equals("Авто", StringComparison.CurrentCultureIgnoreCase)
               || value.Equals("Автоматически", StringComparison.CurrentCultureIgnoreCase);
    }

    public static string BuildLockFromPreference(string? preference, params string?[] contextSamples)
    {
        if (!IsAnyLanguage(preference))
        {
            var language = preference!.Trim();
            return $"""
[LANGUAGE LOCK — CHARACTER SETTING]
This character must speak and narrate in: {language}.
Write the entire in-character reply in that language: actions inside *asterisks*, spoken lines, and brief thoughts.
Do not switch to another language unless a proper name requires it.
""";
        }

        if (contextSamples.Any(LooksRussian))
        {
            return """
[LANGUAGE LOCK — RUSSIAN]
Write the entire in-character reply in clear, natural Russian.
Actions inside *asterisks*, spoken lines, and brief in-character thoughts must all be Russian.
Do not switch into English for narration or dialogue. Proper names may stay as written.
""";
        }

        return """
[LANGUAGE LOCK]
Write the entire in-character reply in the same language as the established dialogue, scenario, and character text above.
Match the dominant language of the recent conversation. Do not switch languages mid-reply.
""";
    }
}
