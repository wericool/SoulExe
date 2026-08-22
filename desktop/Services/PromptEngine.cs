using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class PromptEngine
{
    public PromptBuildResult Build(PromptBuildRequest request)
    {
        var diagnostics = new List<PromptDiagnostic>();
        var messages = new List<LlamaMessage>();
        var systemSections = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Preset?.PromptText))
            systemSections.Add($"[SYSTEM PRESET]\n{request.Preset.PromptText.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Character.SystemPrompt))
            systemSections.Add($"[CHARACTER SYSTEM PROMPT]\n{request.Character.SystemPrompt.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Character.Personality))
        {
            systemSections.Add($"[CHARACTER PERSONALITY]\n{request.Character.Personality.Trim()}");
            systemSections.Add(PersonalityExpressionRule(request.Character.PersonalityExpressionLevel));
        }
        if (!string.IsNullOrWhiteSpace(request.Character.Description))
            systemSections.Add($"[CHARACTER DESCRIPTION]\n{request.Character.Description.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Character.Scenario))
            systemSections.Add($"[SCENARIO]\n{request.Character.Scenario.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.Character.ExampleDialogue))
            systemSections.Add($"[EXAMPLE DIALOGUE]\n{request.Character.ExampleDialogue.Trim()}");
        if (request.Character.UseRoleplayResponseFormatting)
            systemSections.Add(RoleplayResponseFormattingRules);
        if (request.Persona is not null)
        {
            var persona = new StringBuilder($"[USER PROFILE]\nUser: {request.Persona.Name.Trim()}");
            if (!string.IsNullOrWhiteSpace(request.Persona.Description))
                persona.Append("\nDescription: ").Append(request.Persona.Description.Trim());
            if (!string.IsNullOrWhiteSpace(request.Persona.PromptText))
                persona.Append("\nAdditional persona context: ").Append(request.Persona.PromptText.Trim());
            persona.Append("\nTreat this as the user's stable starting identity. Later explicit dialogue, chat summary and memory may add or update facts naturally.");
            systemSections.Add(persona.ToString());
        }

        var startingContext = BuildChatStartingContext(request.Chat);
        if (!string.IsNullOrWhiteSpace(startingContext)) systemSections.Add(startingContext);

        var stateText = BuildStateBlock(request.Character, request.Chat);
        if (!string.IsNullOrWhiteSpace(stateText)) systemSections.Add(stateText);

        var contextText = BuildLoreContext(request, diagnostics);
        if (!string.IsNullOrWhiteSpace(contextText)) systemSections.Add(contextText);

        if (request.IncludeAutoSummary && !string.IsNullOrWhiteSpace(request.Chat.SummaryText))
            systemSections.Add($"[CHAT SUMMARY]\n{request.Chat.SummaryText.Trim()}");
        if (request.IncludeSoulMemory && !string.IsNullOrWhiteSpace(request.Chat.Memory.CharacterMemory))
            systemSections.Add($"[CHARACTER LONG-TERM MEMORY]\n{request.Chat.Memory.CharacterMemory.Trim()}");
        if (request.IncludeSoulMemory && !string.IsNullOrWhiteSpace(request.Chat.Memory.UserProfile))
            systemSections.Add($"[USER & RELATIONSHIP MEMORY]\n{request.Chat.Memory.UserProfile.Trim()}");
        if (request.IncludeSoulMemory)
            foreach (var topic in request.RelevantMemoryTopics)
                systemSections.Add($"[RELEVANT MEMORY: {topic.Key}]\n{topic.Content.Trim()}");
        if (request.IncludeSoulMemory && request.Chat.Memory.Diary.Count > 0)
        {
            var latestDiary = request.Chat.Memory.Diary.OrderByDescending(x => x.CreatedAt).First();
            if (!string.IsNullOrWhiteSpace(latestDiary.Content))
                systemSections.Add($"[CHARACTER PRIVATE REFLECTION]\n{latestDiary.Content.Trim()}");
        }

        messages.Add(new LlamaMessage("system", string.Join("\n\n", systemSections)));
        var history = request.Chat.Messages.OrderBy(x => x.SequenceNumber).ToList();
        if (request.ExcludeLastUserMessage && history.LastOrDefault()?.Role == SoulMessageRole.User) history.RemoveAt(history.Count - 1);
        var contextLimit = Math.Max(1024, request.ContextSize);
        var reservedGeneration = Math.Clamp(request.ReservedGenerationTokens, 64, Math.Max(64, contextLimit - 768));
        var remainingTokens = Math.Max(256, contextLimit - EstimateTokens(messages[0].content) - reservedGeneration - 512);
        foreach (var message in TakeHistoryThatFits(history, remainingTokens, diagnostics))
        {
            var content = CurrentContent(message);
            messages.Add(new LlamaMessage(message.Role == SoulMessageRole.User ? "user" : message.Role == SoulMessageRole.Assistant ? "assistant" : "system", content));
        }
        messages = CollapseConsecutiveAssistantTurns(messages);
        if (request.IsContinuation)
            messages.Add(new LlamaMessage("system", ContinuationDirectorCommand));
        if (request.AppendUserMessage)
        {
            var roleplayFacts = ExtractRoleplayFacts(request.UserMessage);
            if (roleplayFacts.Count > 0)
            {
                messages.Add(new LlamaMessage("system", UserRoleplayEventConvention));
                messages.Add(new LlamaMessage("system", BuildAuthoritativeFactBlock(roleplayFacts)));
            }
            messages.Add(new LlamaMessage("user", request.UserMessage));
        }
        diagnostics.Add(new PromptDiagnostic("context", $"Итоговый контекст: ~{messages.Sum(x => EstimateTokens(x.content))} токенов."));
        return new PromptBuildResult(messages, diagnostics);
    }


    private static string PersonalityExpressionRule(string? level) => (level ?? "natural").Trim().ToLowerInvariant() switch
    {
        "vivid" => "[PERSONALITY EXPRESSION] Make the listed personality traits clearly noticeable in many appropriate reactions, but do not repeat labels or explain them mechanically.",
        "subtle" => "[PERSONALITY EXPRESSION] Keep the listed traits as a quiet background. Reveal them only when the situation naturally invites it; do not name or emphasize them without a reason.",
        _ => "[PERSONALITY EXPRESSION] Express the listed traits naturally and situationally. Do not mention, repeat, or force a trait into every reply; allow varied emotions and reactions."
    };

    private const string RoleplayResponseFormattingRules = """
[RESPONSE FORMAT — READABLE ROLEPLAY]
Use single asterisks only for narrative actions, gestures, scene descriptions and non-verbal reactions: *She steps closer and tilts her head.*
Keep spoken words as plain text; quotation marks are optional. Do not use <think> tags or expose private chain-of-thought.
Do not manually insert blank lines or create separate paragraphs just to distinguish actions, thoughts and speech: SoulExe formats the finished reply locally.
Never write the user's undeclared actions, words, thoughts, choices or feelings for them.
""";

    private const string UserRoleplayEventConvention = """
[AUTHORITATIVE USER ROLEPLAY EVENT]
In the immediately following user message, every fragment enclosed in single asterisks is a director-established fact that has already happened in the fictional scene. It has priority over the character's prior preference, reaction, refusal, mood, or expected choice.
If that fragment states that the character decides, agrees, refuses, changes their mind, answers, acts, feels, or reaches an outcome, do not re-decide it, argue against it, reverse it, or say that the character would not do it. Treat the stated outcome as complete and continue naturally from its consequences in character. This is author-directed scene control, not an invitation to write the user's undeclared actions.
""";

    private const string ContinuationDirectorCommand = """
        [DIRECTOR CONTINUATION COMMAND]
        *continue*
        This is an internal application-level director command, not a message, action, thought, question, request, or established fact from the user.
        Do not quote, discuss, acknowledge, or ask about this command. Continue the current situation naturally from the previous character reply. The previous assistant entry may contain several consecutive parts of the same character's monologue merged by the application; treat all of them as already spoken by the character. Write exactly one fresh in-character reply only; do not write for the user and do not wait for a new user message.
        """;

    private static bool HasRoleplayEvent(string? text)
    {
        return ExtractRoleplayFacts(text).Count > 0;
    }

    private static IReadOnlyList<string> ExtractRoleplayFacts(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var facts = new List<string>();
        var start = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '*') continue;
            var isDoubleMarker = (index > 0 && text[index - 1] == '*') || (index + 1 < text.Length && text[index + 1] == '*');
            if (isDoubleMarker) continue;
            if (start < 0)
            {
                start = index + 1;
                continue;
            }

            var fact = text[start..index].Trim();
            if (!string.IsNullOrWhiteSpace(fact)) facts.Add(fact);
            start = -1;
        }
        return facts;
    }

    private static string BuildAuthoritativeFactBlock(IReadOnlyList<string> facts)
    {
        var exactFacts = string.Join("\n", facts.Select(fact => $"- {fact}"));
        return $$"""
[IRREVOCABLE AUTHOR FACTS FOR THIS TURN]
The following exact facts have already occurred and are true in the scene:
{{exactFacts}}

These facts override any expectation, suspicion, preference, or reaction the character would otherwise have. Preserve every stated outcome literally. Do not deny, question, reverse, reinterpret, discover, or narrate around a stated result. For example, if a fact says another character did not notice something, that character did not notice it and must not imply awareness, suspicion, detection, or later discovery in this reply. Continue only from the consequences of these settled facts. Do not quote this instruction or write for the user.
""";
    }

    private static IEnumerable<SoulMessage> TakeHistoryThatFits(IReadOnlyList<SoulMessage> history, int budget, ICollection<PromptDiagnostic> diagnostics)
    {
        var accepted = new List<SoulMessage>();
        var spent = 0;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var message = history[index];
            var cost = EstimateTokens(CurrentContent(message));
            if (spent + cost > budget)
            {
                diagnostics.Add(new PromptDiagnostic("history", "Старая часть истории не вошла в лимит контекста; её заменяет summary."));
                break;
            }
            accepted.Add(message);
            spent += cost;
        }
        accepted.Reverse();
        return accepted;
    }

    private static List<LlamaMessage> CollapseConsecutiveAssistantTurns(IEnumerable<LlamaMessage> messages)
    {
        var result = new List<LlamaMessage>();
        foreach (var message in messages)
        {
            if (message.role == "assistant" && result.LastOrDefault()?.role == "assistant")
            {
                var previous = result[^1];
                result[^1] = new LlamaMessage("assistant", $"{previous.content}\n\n{message.content}");
            }
            else
            {
                result.Add(message);
            }
        }
        return result;
    }

    private static string BuildChatStartingContext(SoulChat chat)
    {
        var profile = chat.InitialUserProfile?.Trim() ?? "";
        var relationship = chat.InitialRelationshipContext?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(profile) && string.IsNullOrWhiteSpace(relationship)) return "";
        var builder = new StringBuilder("[CHAT STARTING CONTEXT — INITIAL ONLY]");
        if (!string.IsNullOrWhiteSpace(profile)) builder.Append("\nInitial facts about the user: ").Append(profile);
        if (!string.IsNullOrWhiteSpace(relationship)) builder.Append("\nInitial relationship / situation: ").Append(relationship);
        builder.Append("\nThis block describes only the beginning of this specific chat. Later explicit dialogue, CHAT SUMMARY and USER & RELATIONSHIP MEMORY have higher priority and may supersede it. Keep historical beginnings as history, never as an unchanging current relationship.");
        return builder.ToString();
    }

    private static string BuildStateBlock(SoulCharacter character, SoulChat chat)
    {
        if (character.StateVariables.Count == 0) return "";
        var state = new Dictionary<string, string>();
        foreach (var variable in character.StateVariables.OrderBy(x => x.DisplayOrder))
        {
            var value = chat.StateValuesJson.TryGetValue(variable.Id, out var current) ? current : variable.DefaultValueJson;
            state[variable.Key] = value;
        }
        return "[CURRENT STATE VARIABLES]\n" + JsonSerializer.Serialize(state) + "\nUpdate state only through a strict JSON block: <state_update>{\"key\": value}</state_update>.";
    }

    private static string BuildLoreContext(PromptBuildRequest request, ICollection<PromptDiagnostic> diagnostics)
    {
        var selected = new List<SoulLoreEntry>();
        var input = request.UserMessage.ToLowerInvariant();
        foreach (var book in request.Lorebooks)
        {
            foreach (var entry in book.Entries.Where(x => x.IsEnabled))
            {
                var mode = (entry.TriggerMode ?? "always").Trim().ToLowerInvariant();
                var hasKeywords = entry.Keywords.Any(key => !string.IsNullOrWhiteSpace(key));
                var hasSecondaryKeywords = entry.SecondaryKeywords.Any(key => !string.IsNullOrWhiteSpace(key));
                var matched = mode switch
                {
                    "always" or "constant" => true,
                    "random" => Random.Shared.NextDouble() <= Math.Clamp(entry.Probability, 0, 1),
                    // Legacy entries created by the simple editor had the mode "keyword", but no visible
                    // keyword field. Treat those empty-key entries as always active instead of silently dropping them.
                    "keyword" => !hasKeywords || entry.Keywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal)),
                    "secondary" => !hasSecondaryKeywords || entry.SecondaryKeywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal)),
                    _ => !hasKeywords || entry.Keywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                };
                if (matched)
                {
                    selected.Add(entry);
                    var displayMode = mode == "keyword" && !hasKeywords ? "всегда (ключи не заданы)" : entry.TriggerMode;
                    diagnostics.Add(new PromptDiagnostic("lore", $"Активирован лор: {book.Name} / {entry.Name} ({displayMode})."));
                }
            }
        }
        if (selected.Count == 0) return "";
        var builder = new StringBuilder("[ACTIVATED LOREBOOK ENTRIES]");
        foreach (var entry in selected.OrderBy(x => x.InsertionOrder).ThenBy(x => x.Depth))
        {
            var tag = string.Equals(entry.InjectionMode, "active", StringComparison.OrdinalIgnoreCase) ? "SYSTEM DIRECTIVE" : "BACKGROUND KNOWLEDGE";
            builder.Append($"\n\n[{tag}: {entry.Name}]\n{TrimToTokenBudget(entry.Content, entry.TokenBudget)}");
        }
        return builder.ToString();
    }

    private static string CurrentContent(SoulMessage message) =>
        (message.Variants.FirstOrDefault(x => x.Id == message.CurrentVariantId) ?? message.Variants.FirstOrDefault())?.Content ?? "";

    // Conservative approximation for Cyrillic text and chat-template overhead.
    public static int EstimateTokens(string text) => Math.Max(1, ((text?.Length ?? 0) + 1) / 2);
    private static string TrimToTokenBudget(string content, int budget) => content.Length <= budget * 4 ? content : content[..Math.Min(content.Length, budget * 4)] + "…";
}

public sealed record PromptBuildRequest(
    SoulCharacter Character,
    SoulChat Chat,
    SoulPersona? Persona,
    SoulPromptPreset? Preset,
    IReadOnlyList<SoulLorebook> Lorebooks,
    IReadOnlyList<SoulMemoryTopic> RelevantMemoryTopics,
    string UserMessage,
    int ContextSize,
    int ReservedGenerationTokens,
    bool IncludeSoulMemory = true,
    bool IncludeAutoSummary = true,
    bool ExcludeLastUserMessage = true,
    bool AppendUserMessage = true,
    bool IsContinuation = false);

public sealed record PromptBuildResult(IReadOnlyList<LlamaMessage> Messages, IReadOnlyList<PromptDiagnostic> Diagnostics);
public sealed record PromptDiagnostic(string Category, string Text);
