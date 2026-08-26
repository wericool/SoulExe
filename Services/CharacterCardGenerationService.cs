using System.Text.Json;
using SoulExe.Models;

namespace SoulExe.Services;

public sealed record GeneratedCharacterCard(string Name, string Title, string Description, string Personality, string Scenario, string SystemPrompt, string FirstMessage);

/// <summary>
/// Prompt building and parsing for local character-card generation / field expansion.
/// Transport (llama call) stays outside so desktop and network can share the same rules.
/// </summary>
public static class CharacterCardGenerationService
{
    public const string DefaultSystemPrompt =
        "Оставайся в образе персонажа и следуй полям его карточки. Не пиши за пользователя и не повторяй биографию или черты характера без повода.";

    public static IReadOnlyList<LlamaMessage> BuildExpandFieldMessages(string fieldName, string sourceText)
    {
        var prompt = $"""
You are an editor of an AI character card. Extend only the supplied field: {fieldName}.
The field context is mandatory: description needs biographical and visual facts, personality needs traits, motives, habits and speaking manner, scenario needs setting and current situation.
Preserve the language of the supplied text. Return only a concise continuation of 200 to 300 characters; do not repeat the source, do not add a heading, commentary, quotes, meta-text, Markdown, or a character name.
Add concrete, internally consistent details that fit the existing text. Do not invent user actions or dialogue.

Current text:
{sourceText.Trim()}
""";
        return
        [
            new LlamaMessage("system", "You write concise additions for character-card fields. Follow the user's requested language and output only the completed text fragment."),
            new LlamaMessage("user", prompt)
        ];
    }

    public static IReadOnlyList<LlamaMessage> BuildGenerateFromIdeaMessages(string idea)
    {
        var russianInput = idea.Any(character => (character >= 'А' && character <= 'я') || character is 'Ё' or 'ё');
        var languageLock = russianInput
            ? "LANGUAGE LOCK — RUSSIAN ONLY: The user's idea is in Russian. Every JSON string value (name, title, description, personality, scenario, systemPrompt, firstMessage) MUST be written in natural Russian. English text is forbidden, except for unavoidable proper names."
            : "LANGUAGE LOCK: Write every JSON string value in the same language as the user's idea.";
        var prompt = $"""
Create a complete roleplay character card from the user's idea.
{languageLock}
Return STRICT JSON only, without Markdown or explanation, with these string properties: name, title, description, personality, scenario, systemPrompt, firstMessage.
Rules: description, personality and scenario must each be 200 to 300 characters, concrete and mutually consistent. Name is only the character's name. Title is a short role or status. systemPrompt must contain only one or two neutral meta-instructions about staying in character and not writing for the user: NEVER repeat the name, age, city, biography, interests, or individual traits there. Those facts belong only in the dedicated fields. firstMessage starts a scene but does not speak or act for the user. Do not include <think> tags.

User's character idea:
{idea}
""";
        return
        [
            new LlamaMessage("system", $"You generate valid JSON character cards. Return JSON only. {languageLock}"),
            new LlamaMessage("user", prompt)
        ];
    }

    public static string NormalizeFieldAddition(string raw, string source)
    {
        var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (text.StartsWith(source.Trim(), StringComparison.OrdinalIgnoreCase))
            text = text[source.Trim().Length..].TrimStart(' ', '.', ',', ':', ';', '-', '—');
        text = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length > 300)
        {
            var cutoff = text.LastIndexOf(' ', 300);
            text = (cutoff >= 200 ? text[..cutoff] : text[..300]).TrimEnd();
        }
        return text.Trim(' ', '.', ',', ':', ';', '-', '—', '"', '«', '»');
    }

    public static string MergeFieldAddition(string source, string addition)
    {
        var trimmed = source.TrimEnd();
        var separator = trimmed.EndsWith(".", StringComparison.Ordinal)
            || trimmed.EndsWith("!", StringComparison.Ordinal)
            || trimmed.EndsWith("?", StringComparison.Ordinal)
            ? " "
            : ". ";
        return trimmed + separator + addition;
    }

    public static GeneratedCharacterCard? ParseGeneratedCharacter(string raw)
    {
        var text = raw.Replace("<think>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("</think>", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        text = text.Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("```", string.Empty, StringComparison.Ordinal).Trim();
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            var root = document.RootElement;
            static string Value(JsonElement element, string name) =>
                element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                    ? property.GetString()?.Trim() ?? string.Empty
                    : string.Empty;
            return new GeneratedCharacterCard(
                Value(root, "name"),
                Value(root, "title"),
                Value(root, "description"),
                Value(root, "personality"),
                Value(root, "scenario"),
                Value(root, "systemPrompt"),
                Value(root, "firstMessage"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string LimitField(string text, int maxLength)
    {
        text = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length <= maxLength) return text;
        var cutoff = text.LastIndexOf(' ', maxLength);
        return (cutoff > 0 ? text[..cutoff] : text[..maxLength]).TrimEnd();
    }

    public static void ApplyGeneratedCard(SoulCharacter character, GeneratedCharacterCard generated)
    {
        character.Title = generated.Title;
        character.Description = LimitField(generated.Description, 300);
        character.Personality = LimitField(generated.Personality, 300);
        character.Scenario = LimitField(generated.Scenario, 300);
        character.SystemPrompt = DefaultSystemPrompt;
        character.Greetings = [];
        character.UseRoleplayResponseFormatting = true;
    }

    public static (string FieldKey, string FieldName, string SourceText)? ResolveExpandField(SoulCharacter character, string? field)
    {
        var normalized = (field ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "description" => ("description", "описание персонажа", character.Description ?? ""),
            "personality" => ("personality", "личность персонажа", character.Personality ?? ""),
            "scenario" => ("scenario", "сценарий", character.Scenario ?? ""),
            _ => null
        };
    }

    public static void ApplyExpandedField(SoulCharacter character, string fieldKey, string updated)
    {
        switch (fieldKey)
        {
            case "description": character.Description = updated; break;
            case "personality": character.Personality = updated; break;
            case "scenario": character.Scenario = updated; break;
        }
    }
}
