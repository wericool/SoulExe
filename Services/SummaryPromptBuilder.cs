using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Pure prompt builder for direct-chat summaries. Keeping it store- and model-transport-free
/// makes summary behaviour easy to inspect and snapshot-test.
/// </summary>
public static class SummaryPromptBuilder
{
    public static IReadOnlyList<LlamaMessage> Build(string? existingSummary, string? directives, IReadOnlyList<SoulMessage> messages)
    {
        const string system = """
            You are an expert narrative archivist. Update an ongoing story summary using only confirmed dialogue facts. Return plain text only, without commentary.
            Never continue the story or write new dialogue. Preserve earlier established facts while adding the new developments. Keep the entire result under 500 words.
            Use these exact concise sections:
            [CHARACTER STATES & INVENTORY]
            [RELATIONSHIP DYNAMICS]
            [CURRENT SCENE & ATMOSPHERE]
            [KEY DISCOVERIES & LORE]
            [CHRONOLOGICAL EVENTS]
            Retain physical and emotional state, goals, trust, promises, locations, unresolved hooks, important world facts and causal events. Drop only pure filler. Do not invent facts or repeat details.
            """;
        var turns = string.Join("\n", messages.Select(message => $"{(message.Role == SoulMessageRole.User ? "USER" : "CHARACTER")}: {CurrentContent(message)}"));
        var user = $"Existing summary:\n{existingSummary}\n\nDirectives:\n{directives}\n\nNew turns:\n{turns}";
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    private static string CurrentContent(SoulMessage message) =>
        (message.Variants.FirstOrDefault(value => value.Id == message.CurrentVariantId) ?? message.Variants.FirstOrDefault())?.Content ?? "";
}
