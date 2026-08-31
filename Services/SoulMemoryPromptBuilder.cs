using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Pure, locally testable prompt fragments for Soul Memory maintenance jobs.</summary>
public static class SoulMemoryPromptBuilder
{
    public static int MaximumGenerationPasses(bool updateIndex, bool updateDiary, bool updateSummary, bool planTopics)
        => updateIndex || updateDiary || updateSummary || planTopics ? 1 + (planTopics ? 1 : 0) : 0;

    public static IReadOnlyList<LlamaMessage> BuildCognitivePass(CognitivePassPromptInput input, SoulMemoryPresetMode mode)
    {
        var requested = new List<string>();
        var guidance = new List<string>();
        var jsonFields = new List<string> { "\"no_significant_memory_change\":false" };
        if (input.UpdateIndex) requested.Add("character_memory, user_profile and healing_log");
        if (input.UpdateIndex)
        {
            guidance.Add("For character_memory keep a compact, structured account of the character's stable self-view, current emotional state, active agenda and unresolved tension. For user_profile keep confirmed user facts, preferences, relationship dynamics, promises and shared milestones. healing_log briefly records contradictions resolved by newer explicit facts.");
            jsonFields.Add("\"character_memory\":\"...\"");
            jsonFields.Add("\"user_profile\":\"...\"");
            jsonFields.Add("\"healing_log\":\"...\"");
        }
        if (input.UpdateDiary)
        {
            requested.Add("diary_entry");
            guidance.Add("diary_entry is a private 4–6 sentence first-person reflection by the character, with no headings, dialogue, markdown or invented events.");
            jsonFields.Add("\"diary_entry\":\"...\"");
        }
        if (input.UpdateSummary)
        {
            requested.Add("summary");
            guidance.Add("summary must merge the previous summary with the supplied summary turns, stay below 350 words, and use these exact sections: [CHARACTER STATES & INVENTORY], [RELATIONSHIP DYNAMICS], [CURRENT SCENE & ATMOSPHERE], [KEY DISCOVERIES & LORE], [CHRONOLOGICAL EVENTS].");
            jsonFields.Add("\"summary\":\"...\"");
        }
        if (input.PlanTopics)
        {
            requested.Add("topic_plan with at most three create/update actions");
            guidance.Add("topic_plan contains only meaningful durable people, places, objects, promises, secrets or major events. Do not create topics for filler.");
            jsonFields.Add("\"topic_plan\":[{\"action\":\"create\",\"key\":\"short_stable_key\",\"summary\":\"why it matters\"}]");
        }

        var system = $"""
            [SOUL MEMORY — COMBINED COGNITIVE PASS]
            Analyse only the supplied confirmed dialogue. Return one valid JSON object and nothing else.
            This run is allowed to update only: {string.Join(", ", requested)}.
            Fields that are not requested must be omitted. Never update or fabricate a disabled memory component.

            {string.Join("\n", guidance)}

            If memory has no meaningful new facts, set no_significant_memory_change to true. This does not cancel a requested summary or diary.
            Requested profile: {mode.DisplayName}.
            """;

        var blocks = new List<string> { $"CHARACTER: {input.CharacterName}" };
        if (input.UpdateIndex)
            blocks.Add($"CHAT STARTING CONTEXT (initial only; newer confirmed dialogue overrides it):\nUSER PROFILE: {input.InitialUserProfile}\nRELATIONSHIP: {input.InitialRelationshipContext}\n\nCURRENT CHARACTER MEMORY:\n{input.CharacterMemory}\n\nCURRENT USER & RELATIONSHIP MEMORY:\n{input.UserProfile}");
        else if (input.UpdateDiary)
            blocks.Add($"CURRENT CHARACTER MEMORY (context for the diary only; do not update it):\n{input.CharacterMemory}");
        if (input.PlanTopics && input.RelevantTopics.Count > 0)
            blocks.Add("RELEVANT TOPIC MEMORIES:\n" + string.Join("\n\n", input.RelevantTopics.Select(topic => $"[{topic.Key}]\n{topic.Content}")));
        if ((input.UpdateIndex || input.UpdateDiary || input.PlanTopics) && !string.IsNullOrWhiteSpace(input.LoreContext))
            blocks.Add("RELEVANT WORLD LORE:\n" + input.LoreContext);
        if (input.MemoryDialogue.Count > 0)
            blocks.Add("NEW MEMORY DIALOGUE:\n" + FormatDialogue(input.MemoryDialogue));
        if (input.UpdateSummary)
            blocks.Add($"CURRENT STORY SUMMARY:\n{input.ExistingSummary}\n\nSUMMARY-SPECIFIC INSTRUCTIONS:\n{input.SummaryDirectives}\n\nNEW SUMMARY TURNS:\n{FormatDialogue(input.SummaryDialogue)}");
        blocks.Add("JSON SHAPE (omit disabled fields):\n{" + string.Join(",", jsonFields) + "}");
        var user = string.Join("\n\n", blocks);
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    public static IReadOnlyList<LlamaMessage> BuildArchivistBatch(
        string characterName,
        string? characterMemory,
        IReadOnlyList<CognitiveTopicPlan> plans,
        IReadOnlyList<SoulMemoryTopic> existingTopics,
        IEnumerable<SoulMessage> dialogue)
    {
        const string system = """
            [SOUL MEMORY — BATCH ARCHIVIST]
            Update every requested long-term topic in one pass. Return one valid JSON object and nothing else.
            Output exactly one item for every supplied plan, using its key unchanged.
            Each content value must be concise (at most 220 words), factual and durable. Merge existing content, replace contradicted facts with newer confirmed facts, and never invent information.
            JSON shape: {"topic_updates":[{"key":"unchanged_key","content":"complete updated topic text"}]}
            """;
        var planText = string.Join("\n", plans.Select(plan => $"- {plan.Action} | {plan.Key} | {plan.Summary}"));
        var existing = string.Join("\n\n", plans.Select(plan =>
        {
            var content = existingTopics.FirstOrDefault(topic => string.Equals(topic.Key, plan.Key, StringComparison.OrdinalIgnoreCase))?.Content ?? "(new topic)";
            return $"[{plan.Key}]\n{content}";
        }));
        var user = $"""
            CHARACTER: {characterName}

            CURRENT CHARACTER MEMORY:
            {characterMemory}

            PLANS:
            {planText}

            EXISTING TOPICS:
            {existing}

            RECENT DIALOGUE:
            {FormatDialogue(dialogue)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

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

public sealed record CognitivePassPromptInput(
    string CharacterName,
    string InitialUserProfile,
    string InitialRelationshipContext,
    string CharacterMemory,
    string UserProfile,
    string ExistingSummary,
    string SummaryDirectives,
    string LoreContext,
    IReadOnlyList<SoulMemoryTopic> RelevantTopics,
    IReadOnlyList<SoulMessage> MemoryDialogue,
    IReadOnlyList<SoulMessage> SummaryDialogue,
    bool UpdateIndex,
    bool UpdateDiary,
    bool UpdateSummary,
    bool PlanTopics);

public sealed record CognitiveTopicPlan(string Action, string Key, string Summary);

public sealed record SoulMemoryRouterPromptInput(
    string CharacterName,
    string InitialUserProfile,
    string InitialRelationshipContext,
    string CharacterMemory,
    string UserProfile,
    IReadOnlyList<SoulMemoryTopic> Topics,
    IReadOnlyList<SoulMessage> Delta);
