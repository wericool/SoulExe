using System;
using System.Collections.Generic;

namespace SoulExe.Services;

/// <summary>
/// Presentation pass applied after model generation for every conversation mode
/// (direct chat, scene, desktop, network, embedded web).
/// </summary>
public static class ResponseFormatter
{
    public static string RemoveOwnLeadingLabel(string text, string characterName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(characterName)) return text?.Trim() ?? "";
        var result = text.TrimStart();
        var bare = characterName.Trim();
        var labels = new[] { $"{bare}:", $"{bare}：", $"[{bare}]", $"[{bare}]:", $"[{bare}]：" };
        foreach (var label in labels)
        {
            if (result.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                return result[label.Length..].TrimStart();
        }
        return result;
    }

    public static string NormalizeRoleplayLayout(string text, bool enabled)
    {
        var source = text?.Trim() ?? "";
        if (!enabled || string.IsNullOrWhiteSpace(source)) return source;

        var paragraphs = new List<string>();
        var cursor = 0;
        while (cursor < source.Length)
        {
            var opening = source.IndexOf('*', cursor);
            if (opening < 0)
            {
                AddPlainParagraph(paragraphs, source[cursor..]);
                break;
            }

            AddPlainParagraph(paragraphs, source[cursor..opening]);
            var closing = source.IndexOf('*', opening + 1);
            if (closing < 0)
            {
                AddPlainParagraph(paragraphs, source[opening..]);
                break;
            }

            var italic = source[(opening + 1)..closing].Trim();
            if (!string.IsNullOrWhiteSpace(italic)) paragraphs.Add($"*{italic}*");
            cursor = closing + 1;
        }

        return paragraphs.Count == 0 ? source : string.Join("\n\n", paragraphs);
    }

    /// <summary>
    /// Full post-generation pass used by scene turns: strip state blocks externally first,
    /// then remove a leaked speaker label and normalize roleplay layout.
    /// </summary>
    public static string NormalizeSceneReply(string raw, string characterName, bool roleplayFormattingEnabled, Func<string, string>? stripStateBlocks = null)
    {
        var text = raw ?? "";
        if (stripStateBlocks is not null) text = stripStateBlocks(text);
        text = RemoveOwnLeadingLabel(text, characterName);
        return NormalizeRoleplayLayout(text, roleplayFormattingEnabled);
    }

    private static void AddPlainParagraph(ICollection<string> paragraphs, string source)
    {
        var plain = source.Trim();
        if (!string.IsNullOrWhiteSpace(plain)) paragraphs.Add(plain);
    }
}

/// <summary>Compatibility alias — existing call sites keep compiling.</summary>
public static class SceneResponseFormatter
{
    public static string RemoveOwnLeadingLabel(string text, string characterName) =>
        ResponseFormatter.RemoveOwnLeadingLabel(text, characterName);

    public static string NormalizeRoleplayLayout(string text, bool enabled) =>
        ResponseFormatter.NormalizeRoleplayLayout(text, enabled);
}
