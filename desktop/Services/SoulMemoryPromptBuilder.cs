using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Pure, locally testable prompt fragments for Soul Memory maintenance jobs.</summary>
public static class SoulMemoryPromptBuilder
{
    public static IReadOnlyList<LlamaMessage> BuildArchivist(string characterName, string? characterMemory, string topicKey, string action, string reason, string? existingTopicContent, IEnumerable<SoulMessage> dialogue)
    {
        const string system = """
            [SOUL MEMORY — ARCHIVIST]
            Write or update exactly one long-term topic memory. Return plain text only: no JSON, no markdown wrapper and no conversation.
            Keep it under 300 words. Store only confirmed, durable details: people, places, objects, promises, secrets, major events and their consequences.
            Merge with existing content, replacing facts contradicted by newer dialogue. Do not add filler or invent information.
            """;
        var user = $"""
            CHARACTER: {characterName}
            TOPIC KEY: {topicKey}
            ACTION: {action}
            REASON: {reason}

            CURRENT CHARACTER MEMORY:
            {characterMemory}

            EXISTING TOPIC CONTENT:
            {existingTopicContent}

            RECENT DIALOGUE:
            {FormatDialogue(dialogue)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    public static IReadOnlyList<LlamaMessage> BuildRouter(SoulMemoryRouterPromptInput input, SoulMemoryPresetMode mode)
    {
        var topicRule = mode.UpdatesTopics
            ? "Return topic_plan with at most five meaningful create or update actions. Each action has action (create/update), key (short stable slug) and summary. Do not create topics for filler."
            : "Do not return topic_plan; topics are disabled in this preset.";
        var system = """
            [SOUL MEMORY — ROUTER]
            You are a local analytical memory engine for a roleplay character. Return ONE valid JSON object and nothing else.
            Work only from confirmed dialogue facts. Never invent memories. Preserve important prior facts unless new dialogue contradicts them.
            The chat starting context is an initial baseline, not a permanent current fact. If later explicit dialogue gives a name, changes the relationship or contradicts that baseline, the newer dialogue must replace it in user_profile; preserve the old situation only as history when relevant.
            Update character_memory with the character's stable self-view, current emotional state, active agenda and unresolved tension.
            Update user_profile with known user facts, preferences, relationship dynamics, promises and shared milestones.
            healing_log records contradictions resolved by newer facts.
            For greetings, filler, repeated content or no meaningful change, return only {"no_significant_change":true}.
            {TOPIC_RULE}
            The JSON schema is:
            {"no_significant_change":false,"character_memory":"compact structured memory","user_profile":"compact structured relationship profile","healing_log":"resolved contradictions or No conflicts detected.","topic_plan":[{"action":"create","key":"short_slug","summary":"why this topic matters"}]}
            Active preset: {PRESET}.
            """
            .Replace("{TOPIC_RULE}", topicRule, StringComparison.Ordinal)
            .Replace("{PRESET}", mode.DisplayName, StringComparison.Ordinal);
        var relevant = string.Join("\n\n", input.Topics.Take(3).Select(topic => $"[{topic.Key}]\n{topic.Content}"));
        var user = $"""
            CHARACTER: {input.CharacterName}

            CHAT STARTING CONTEXT (initial only; newer dialogue overrides it):
            USER PROFILE: {input.InitialUserProfile}
            RELATIONSHIP: {input.InitialRelationshipContext}

            CURRENT CHARACTER MEMORY:
            {input.CharacterMemory}

            CURRENT USER PROFILE:
            {input.UserProfile}

            RECENT TOPIC MEMORIES:
            {relevant}

            NEW DIALOGUE DELTA:
            {FormatDialogue(input.Delta)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    public static IReadOnlyList<LlamaMessage> BuildDiary(string characterName, string? characterMemory, IEnumerable<SoulMessage> dialogue)
    {
        var system = $"""
            [SOUL MEMORY — PRIVATE DIARY]
            You are {characterName}. Write a private reflection on the recent dialogue.
            Return only 4–6 first-person sentences. Focus on emotions, doubts, hopes and evolving feelings toward the user.
            Do not use asterisks, dialogue, headings, markdown or <think> tags. Do not invent events.
            """;
        var user = $"""
            CURRENT CHARACTER MEMORY:
            {characterMemory}

            RECENT DIALOGUE:
            {FormatDialogue(dialogue)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    private static string FormatDialogue(IEnumerable<SoulMessage> messages) => string.Join("\n", messages.Select(message => $"{(message.Role == SoulMessageRole.User ? "USER" : message.Role == SoulMessageRole.Assistant ? "CHARACTER" : "SYSTEM")}: {CurrentContent(message)}"));
    private static string CurrentContent(SoulMessage message) => (message.Variants.FirstOrDefault(variant => variant.Id == message.CurrentVariantId) ?? message.Variants.FirstOrDefault())?.Content ?? "";
}

public sealed record SoulMemoryRouterPromptInput(
    string CharacterName,
    string InitialUserProfile,
    string InitialRelationshipContext,
    string CharacterMemory,
    string UserProfile,
    IReadOnlyList<SoulMemoryTopic> Topics,
    IReadOnlyList<SoulMessage> Delta);
