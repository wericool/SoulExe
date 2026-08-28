using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Single prompt builder for every conversation mode. Direct chats and two-character scenes
/// share character blocks, lore selection helpers, roleplay rules and context budgeting;
/// mode-specific sections stay in dedicated private methods.
/// </summary>
public sealed class ConversationPromptEngine
{
    public PromptBuildResult BuildDirect(PromptBuildRequest request) => BuildDirectCore(request);

    public PromptBuildResult BuildGroup(GroupPromptBuildRequest request) => BuildGroupCore(request);

    // -------------------------------------------------------------------------
    // Direct (user ↔ one character)
    // -------------------------------------------------------------------------

    private static PromptBuildResult BuildDirectCore(PromptBuildRequest request)
    {
        var diagnostics = new List<PromptDiagnostic>();
        var systemSections = new List<string>();
        var budgetPlan = ContextBudgetPlan.Create(
            request.ContextSize,
            request.ReservedGenerationTokens,
            safetyReserve: 512,
            newestMessageTokens: ConversationContextWindow.EstimateTokens(request.UserMessage));
        var baseSectionBudget = Math.Max(64, budgetPlan.BaseContextTokens / 3);

        if (!string.IsNullOrWhiteSpace(request.Preset?.PromptText))
            systemSections.Add(BuildBoundedBaseContextBlock("SYSTEM PRESET", request.Preset.PromptText, baseSectionBudget, diagnostics));

        AppendCharacterIdentity(systemSections, request.Character, includeScenario: true, includeExampleDialogue: true, budgetPlan.CharacterTokens, diagnostics);

        if (request.Character.UseRoleplayResponseFormatting)
            systemSections.Add(PromptRules.RoleplayFormat(ConversationKind.Direct));

        if (request.Persona is not null)
            systemSections.Add(BuildBoundedBaseContextBlock("USER PROFILE", BuildPersonaBlock(request.Persona), baseSectionBudget, diagnostics));

        var startingContext = BuildChatStartingContext(request.Conversation);
        if (!string.IsNullOrWhiteSpace(startingContext)) systemSections.Add(BuildBoundedBaseContextBlock("CHAT STARTING CONTEXT", startingContext, baseSectionBudget, diagnostics));

        var stateText = BuildStateBlock(request.Character, request.Conversation, budgetPlan.StateTokens, diagnostics);
        if (!string.IsNullOrWhiteSpace(stateText)) systemSections.Add(stateText);

        var loreText = BuildLoreContext(request.Lorebooks, BuildLoreTriggerText(request), "direct_recent_and_current", diagnostics, budgetPlan.LoreTokens);
        if (!string.IsNullOrWhiteSpace(loreText)) systemSections.Add(loreText);

        if (request.IncludeAutoSummary && !string.IsNullOrWhiteSpace(request.Conversation.SummaryText))
            systemSections.Add(BuildBoundedSummary("CHAT SUMMARY", request.Conversation.SummaryText, budgetPlan.SummaryTokens, diagnostics));

        if (request.IncludeSoulMemory)
            AppendSoulMemory(systemSections, request.Conversation, request.RelevantMemoryTopics, budgetPlan.MemoryTokens, diagnostics);

        systemSections.Add(BuildDirectLanguageLock(request));

        var messages = new List<LlamaMessage>
        {
            new("system", string.Join("\n\n", systemSections.Where(s => !string.IsNullOrWhiteSpace(s))))
        };

        var history = request.Conversation.Messages.OrderBy(x => x.SequenceNumber).ToList();
        if (request.ExcludeLastUserMessage && history.LastOrDefault() is { } last && !IsCharacterMessage(request.Conversation, last))
            history.RemoveAt(history.Count - 1);

        var remainingTokens = ConversationContextWindow.CalculateHistoryBudget(
            request.ContextSize, messages[0].content, request.ReservedGenerationTokens, 512);

        foreach (var message in ConversationContextWindow.TakeLatestThatFits(
                     history, remainingTokens, CurrentContent,
                     () => diagnostics.Add(new PromptDiagnostic("history", "Старая часть истории не вошла в лимит контекста; её заменяет summary."))))
        {
            var content = CurrentContent(message);
            if (message.AuthorKind == SoulMessageAuthorKind.Persona)
                content = $"[PERSONA: {message.AuthorName}]\n{content}";
            else if (message.AuthorKind == SoulMessageAuthorKind.Director)
                content = $"[DIRECTOR EVENT] [CANONICAL AND UNCONDITIONAL FACT. It is already true in the scene. Never question, soften, reinterpret, ignore, or undo it.]\n{content}";
            messages.Add(new LlamaMessage(
                message.AuthorKind == SoulMessageAuthorKind.Director ? "system" :
                IsCharacterMessage(request.Conversation, message) ? "assistant" : "user",
                content));
        }

        messages = PromptRules.CollapseConsecutiveAssistantTurns(messages);

        if (request.IsContinuation)
            messages.Add(new LlamaMessage("system", PromptRules.ContinuationDirectorCommand));

        // The caller persists a message before prompt construction. Reusing the
        // canonical history preserves its true author: in particular a director
        // event stays a system instruction instead of being appended as a user turn.
        if (request.AppendUserMessage)
            messages.Add(new LlamaMessage("user", request.UserMessage));

        diagnostics.Add(new PromptDiagnostic("budget",
            $"Бюджет: ввод ~{budgetPlan.InputBudget}, резерв истории ~{budgetPlan.ReservedHistoryTokens}, базовый контекст до {budgetPlan.BaseContextTokens}, лор до {budgetPlan.LoreTokens}, summary до {budgetPlan.SummaryTokens}, память до {budgetPlan.MemoryTokens} токенов."));
        diagnostics.Add(new PromptDiagnostic("context",
            $"Итоговый контекст: ~{messages.Sum(x => ConversationContextWindow.EstimateTokens(x.content))} токенов."));
        return new PromptBuildResult(messages, diagnostics);
    }

    // -------------------------------------------------------------------------
    // Scene (two characters + optional director events)
    // -------------------------------------------------------------------------

    private static PromptBuildResult BuildGroupCore(GroupPromptBuildRequest request)
    {
        var scene = request.Conversation;
        var active = request.ActiveCharacterId == request.First.Id ? request.First : request.Second;
        var counterpart = active.Id == request.First.Id ? request.Second : request.First;
        var diagnostics = new List<PromptDiagnostic>();
        var systems = new List<string>();
        var normalizedContextSize = Math.Max(1024, request.ContextSize);
        var normalizedReservedGeneration = Math.Clamp(request.ReservedGenerationTokens, 64, Math.Max(64, normalizedContextSize - 768));
        var tokenizerSafetyReserve = Math.Max(768, Math.Min(1536, Math.Max(256, normalizedReservedGeneration / 2)));
        var budgetPlan = ContextBudgetPlan.Create(request.ContextSize, request.ReservedGenerationTokens, tokenizerSafetyReserve);

        if (scene.TurnState?.EnforceContract ?? true) systems.Add(PromptRules.SceneContract);
        if (scene.TurnState?.AdvanceAndAvoidRepetition ?? true) systems.Add(PromptRules.SceneProgressionRules);
        systems.Add(BuildSceneState(scene, budgetPlan.StateTokens, diagnostics));
        systems.Add(BuildSceneSpeakerBlock(active, budgetPlan.CharacterTokens, diagnostics));
        systems.Add(BuildCounterpartBlock(counterpart, Math.Max(128, budgetPlan.CharacterTokens / 2), diagnostics));
        var personaParticipants = scene.Messages
            .Where(message => message.AuthorKind == SoulMessageAuthorKind.Persona && message.AuthorPersonaId is not null)
            .GroupBy(message => message.AuthorPersonaId!.Value)
            .Select(group => new { Id = group.Key, Name = group.Last().AuthorName })
            .ToList();
        foreach (var participant in personaParticipants)
        {
            var persona = request.Personas is not null && request.Personas.TryGetValue(participant.Id, out var found)
                ? found
                : null;
            var identity = persona is null
                ? $"{participant.Name} is a third, user-controlled speaking participant in this scene."
                : BuildPersonaBlock(persona) + "\n\nThis persona is a third, user-controlled speaking participant in this scene. Their [PERSONA SPEECH] turns are ordinary in-world dialogue, never director instructions or system events; notice and respond to them naturally.";
            systems.Add(BuildBoundedBaseContextBlock("USER PERSONA PARTICIPANT", identity, Math.Max(96, budgetPlan.BaseContextTokens / 3), diagnostics));
        }

        if (active.UseRoleplayResponseFormatting)
            systems.Add(PromptRules.RoleplayFormat(ConversationKind.Scene));

        var lorebooks = active.LorebookIds
            .Where(request.Lorebooks.ContainsKey)
            .Select(id => request.Lorebooks[id])
            .ToList();
        var lore = BuildLoreContext(lorebooks, BuildSceneLoreTriggerText(scene), "scene_recent_turns", diagnostics, budgetPlan.LoreTokens);
        if (!string.IsNullOrWhiteSpace(lore)) systems.Add(lore);

        if (!string.IsNullOrWhiteSpace(scene.SummaryText))
            systems.Add(BuildBoundedSummary("SHARED SCENE SUMMARY", scene.SummaryText, budgetPlan.SummaryTokens, diagnostics));

        systems.Add(BuildSceneLanguageLock(scene, active, counterpart));

        var result = new List<LlamaMessage>
        {
            new("system", string.Join("\n\n", systems.Where(text => !string.IsNullOrWhiteSpace(text))))
        };

        var tokenBudget = ConversationContextWindow.CalculateHistoryBudget(
            normalizedContextSize, result[0].content, normalizedReservedGeneration, tokenizerSafetyReserve);

        var history = new List<LlamaMessage>();
        foreach (var message in scene.Messages.OrderBy(message => message.SequenceNumber))
        {
            var participant = scene.FindParticipant(message.AuthorParticipantId);
            var role = message.Kind == ConversationMessageKind.DirectorEvent ? "system"
                : participant?.CharacterId == active.Id ? "assistant" : "user";
            var prefix = message.Kind == ConversationMessageKind.DirectorEvent ? "[DIRECTOR EVENT] [CANONICAL AND UNCONDITIONAL FACT. It is already true in the scene. Never question, soften, reinterpret, ignore, or undo it.] "
                : message.AuthorPersonaId is not null ? $"[PERSONA SPEECH: {message.AuthorName}] " : "";
            history.Add(new LlamaMessage(role, prefix + CurrentContent(message)));
        }

        result.AddRange(ConversationContextWindow.TakeLatestThatFits(history, tokenBudget, message => message.content));
        result.Add(new LlamaMessage("user", BuildSceneTurnInstruction(scene, active, counterpart)));
        diagnostics.Add(new PromptDiagnostic("budget",
            $"Бюджет: ввод ~{budgetPlan.InputBudget}, резерв истории ~{budgetPlan.ReservedHistoryTokens}, лор до {budgetPlan.LoreTokens}, summary до {budgetPlan.SummaryTokens}, память до {budgetPlan.MemoryTokens} токенов."));
        diagnostics.Add(new PromptDiagnostic("context",
            $"Итоговый контекст: ~{result.Sum(message => ConversationContextWindow.EstimateTokens(message.content))} токенов."));
        return new PromptBuildResult(result, diagnostics);
    }

    // -------------------------------------------------------------------------
    // Shared builders
    // -------------------------------------------------------------------------

    private static void AppendCharacterIdentity(List<string> sections, SoulCharacter character, bool includeScenario, bool includeExampleDialogue, int totalTokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var spent = 0;
        void Add(string label, string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            var remaining = totalTokenBudget - spent;
            if (remaining <= 0)
            {
                diagnostics.Add(new PromptDiagnostic("character", $"Блок карточки {label} не вошёл в лимит {totalTokenBudget} токенов."));
                return;
            }
            var clean = content.Trim();
            var bounded = PromptRules.TrimToTokenBudget(clean, remaining);
            spent += Math.Min(remaining, ConversationContextWindow.EstimateTokens(clean));
            if (bounded.Length < clean.Length)
                diagnostics.Add(new PromptDiagnostic("character", $"Блок карточки {label} обрезан, чтобы сохранить место истории."));
            sections.Add($"[{label}]\n{bounded}");
        }

        Add("CHARACTER SYSTEM PROMPT", character.SystemPrompt);
        if (!string.IsNullOrWhiteSpace(character.Personality))
        {
            Add("CHARACTER PERSONALITY", character.Personality);
            Add("PERSONALITY EXPRESSION", PromptRules.PersonalityExpressionRule(character.PersonalityExpressionLevel));
        }
        Add("CHARACTER DESCRIPTION", character.Description);
        if (includeScenario) Add("SCENARIO", character.Scenario);
        if (includeExampleDialogue) Add("EXAMPLE DIALOGUE", character.ExampleDialogue);
    }

    private static string BuildPersonaBlock(SoulPersona persona)
    {
        var builder = new StringBuilder($"[USER PROFILE]\nUser: {persona.Name.Trim()}");
        if (!string.IsNullOrWhiteSpace(persona.Description))
            builder.Append("\nDescription: ").Append(persona.Description.Trim());
        if (!string.IsNullOrWhiteSpace(persona.PromptText))
            builder.Append("\nAdditional persona context: ").Append(persona.PromptText.Trim());
        builder.Append("\nTreat this as the user's stable starting identity. Later explicit dialogue, chat summary and memory may add or update facts naturally.");
        return builder.ToString();
    }

    private static string BuildBoundedBaseContextBlock(string label, string? content, int tokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var clean = content?.Trim() ?? string.Empty;
        var bounded = PromptRules.TrimToTokenBudget(clean, tokenBudget);
        if (bounded.Length < clean.Length)
            diagnostics.Add(new PromptDiagnostic("base_context", $"Блок {label} обрезан до {tokenBudget} токенов, чтобы сохранить место истории."));
        return bounded;
    }

    private static string BuildChatStartingContext(ConversationSnapshot chat)
    {
        var profile = chat.Context.InitialUserProfile?.Trim() ?? "";
        var relationship = chat.Context.InitialRelationshipContext?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(profile) && string.IsNullOrWhiteSpace(relationship)) return "";
        var builder = new StringBuilder("[CHAT STARTING CONTEXT — INITIAL ONLY]");
        if (!string.IsNullOrWhiteSpace(profile)) builder.Append("\nInitial facts about the user: ").Append(profile);
        if (!string.IsNullOrWhiteSpace(relationship)) builder.Append("\nInitial relationship / situation: ").Append(relationship);
        builder.Append("\nThis block describes only the beginning of this specific chat. Later explicit dialogue, CHAT SUMMARY and USER & RELATIONSHIP MEMORY have higher priority and may supersede it. Keep historical beginnings as history, never as an unchanging current relationship.");
        return builder.ToString();
    }

    private static string BuildStateBlock(SoulCharacter character, ConversationSnapshot chat, int tokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        if (character.StateVariables.Count == 0) return "";
        var state = new Dictionary<string, string>();
        foreach (var variable in character.StateVariables.OrderBy(x => x.DisplayOrder))
        {
            var value = chat.Context.StateValues.TryGetValue(variable.Id, out var current) ? current : variable.DefaultValueJson;
            state[variable.Key] = value;
        }
        return BuildBoundedStateBlock("CURRENT STATE VARIABLES", "[CURRENT STATE VARIABLES]\n" + JsonSerializer.Serialize(state)
               + "\nUpdate state only through a strict JSON block: <state_update>{\"key\": value}</state_update>.", tokenBudget, diagnostics);
    }

    private static void AppendSoulMemory(List<string> sections, ConversationSnapshot chat, IReadOnlyList<SoulMemoryTopic> topics, int totalTokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var spent = 0;
        void Add(string label, string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            var remaining = totalTokenBudget - spent;
            if (remaining <= 0)
            {
                diagnostics.Add(new PromptDiagnostic("memory", $"Блок памяти {label} не вошёл в лимит {totalTokenBudget} токенов."));
                return;
            }
            var clean = content.Trim();
            var bounded = PromptRules.TrimToTokenBudget(clean, remaining);
            spent += Math.Min(remaining, ConversationContextWindow.EstimateTokens(clean));
            if (bounded.Length < clean.Length)
                diagnostics.Add(new PromptDiagnostic("memory", $"Блок памяти {label} обрезан, чтобы сохранить место истории."));
            sections.Add($"[{label}]\n{bounded}");
        }

        var memory = chat.Context.Memory ?? new SoulMemoryBundle();
        Add("CHARACTER LONG-TERM MEMORY", memory.CharacterMemory);
        Add("USER & RELATIONSHIP MEMORY", memory.UserProfile);
        foreach (var topic in topics)
            Add($"RELEVANT MEMORY: {topic.Key}", topic.Content);
        if (memory.Diary.Count > 0)
        {
            var latestDiary = memory.Diary.OrderByDescending(x => x.CreatedAt).First();
            Add("CHARACTER PRIVATE REFLECTION", latestDiary.Content);
        }
    }

    private static string BuildLoreContext(IReadOnlyList<SoulLorebook> lorebooks, string userMessage, string triggerSource, ICollection<PromptDiagnostic> diagnostics, int totalTokenBudget)
    {
        var selected = new List<SoulLoreEntry>();
        var input = (userMessage ?? "").ToLowerInvariant();
        foreach (var book in lorebooks)
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
                    // Legacy keyword mode with empty keys → treat as always.
                    "keyword" => !hasKeywords || entry.Keywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal)),
                    "secondary" => !hasSecondaryKeywords || entry.SecondaryKeywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal)),
                    _ => !hasKeywords || entry.Keywords.Any(key => input.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                };
                if (matched)
                {
                    selected.Add(entry);
                    var displayMode = mode == "keyword" && !hasKeywords ? "всегда (ключи не заданы)" : entry.TriggerMode;
                    diagnostics.Add(new PromptDiagnostic("lore", $"Активирован лор: {book.Name} / {entry.Name} ({displayMode}; trigger={triggerSource})."));
                }
            }
        }
        if (selected.Count == 0) return "";
        var builder = new StringBuilder("[ACTIVATED LOREBOOK ENTRIES]");
        var spent = 0;
        var skipped = 0;
        foreach (var entry in selected.OrderBy(x => x.InsertionOrder).ThenBy(x => x.Depth))
        {
            var entryBudget = Math.Max(1, entry.TokenBudget);
            var remaining = totalTokenBudget - spent;
            if (remaining <= 0) { skipped++; continue; }
            var allowedBudget = Math.Min(entryBudget, remaining);
            var tag = string.Equals(entry.InjectionMode, "active", StringComparison.OrdinalIgnoreCase)
                ? "SYSTEM DIRECTIVE" : "BACKGROUND KNOWLEDGE";
            builder.Append($"\n\n[{tag}: {entry.Name}]\n{PromptRules.TrimToTokenBudget(entry.Content, allowedBudget)}");
            spent += ConversationContextWindow.EstimateTokens(entry.Content) > allowedBudget ? allowedBudget : ConversationContextWindow.EstimateTokens(entry.Content);
        }
        if (skipped > 0)
            diagnostics.Add(new PromptDiagnostic("lore", $"Лор: {skipped} записей не вошли в общий лимит {totalTokenBudget} токенов."));
        return builder.ToString();
    }

    /// <summary>
    /// Keyword lore should see a little conversational continuity: users naturally refer to an
    /// already-mentioned person or place as "he", "there" or "that". The window is deliberately
    /// short, so an old topic does not keep activating unrelated lore forever.
    /// </summary>
    private static string BuildLoreTriggerText(PromptBuildRequest request)
    {
        var recent = request.Conversation.Messages
            .OrderByDescending(message => message.SequenceNumber)
            .Take(3)
            .Reverse()
            .Select(CurrentContent)
            .Where(content => !string.IsNullOrWhiteSpace(content));
        return string.Join("\n", recent.Append(request.UserMessage ?? string.Empty));
    }

    private static string BuildSceneState(ConversationSnapshot scene, int tokenBudget, ICollection<PromptDiagnostic> diagnostics) => BuildBoundedStateBlock("SCENE", $"""
        [SCENE]
        Scenario: {scene.Context.Scenario}
        Location: {scene.Context.Location}
        Time: {scene.Context.TimeContext}
        Mood: {scene.Context.Mood}
        Current goal: {scene.Context.Goal}
        Shared relationship / established context: {scene.Context.RelationshipContext}
        """, tokenBudget, diagnostics);

    private static string BuildBoundedStateBlock(string label, string content, int tokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var clean = content.Trim();
        var bounded = PromptRules.TrimToTokenBudget(clean, tokenBudget);
        if (bounded.Length < clean.Length)
            diagnostics.Add(new PromptDiagnostic("state", $"Блок состояния {label} обрезан, чтобы сохранить место истории."));
        return bounded;
    }

    private static string BuildSceneSpeakerBlock(SoulCharacter character, int tokenBudget, ICollection<PromptDiagnostic> diagnostics) => BuildBoundedCharacterBlock("CURRENT SPEAKER", $"""
        [YOU — CURRENT SPEAKER: {character.Name}]
        Title: {character.Title}
        Description: {character.Description}
        Personality: {character.Personality}
        Character instructions: {character.SystemPrompt}
        """, tokenBudget, diagnostics);

    private static string BuildCounterpartBlock(SoulCharacter character, int tokenBudget, ICollection<PromptDiagnostic> diagnostics) => BuildBoundedCharacterBlock("COUNTERPART", $"""
        [COUNTERPART: {character.Name}]
        Description: {character.Description}
        Personality: {character.Personality}
        Treat this as the other participant. Never write, choose, think, feel or act for them.
        """, tokenBudget, diagnostics);

    private static string BuildBoundedCharacterBlock(string label, string content, int tokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var clean = content.Trim();
        var bounded = PromptRules.TrimToTokenBudget(clean, tokenBudget);
        if (bounded.Length < clean.Length)
            diagnostics.Add(new PromptDiagnostic("character", $"Блок карточки {label} обрезан, чтобы сохранить место истории."));
        return bounded;
    }

    private static string BuildSceneLoreTriggerText(ConversationSnapshot scene) => string.Join("\n", scene.Messages
        .OrderByDescending(message => message.SequenceNumber)
        .Take(4)
        .Reverse()
        .Select(CurrentContent)
        .Where(content => !string.IsNullOrWhiteSpace(content)));

    private static string BuildBoundedSummary(string label, string text, int tokenBudget, ICollection<PromptDiagnostic> diagnostics)
    {
        var clean = text.Trim();
        var content = PromptRules.TrimToTokenBudget(clean, tokenBudget);
        if (content.Length < clean.Length)
            diagnostics.Add(new PromptDiagnostic("summary", $"Summary обрезана до {tokenBudget} токенов, чтобы сохранить место свежей истории."));
        return $"[{label}]\n{content}";
    }

    private static string BuildSceneTurnInstruction(ConversationSnapshot scene, SoulCharacter active, SoulCharacter counterpart)
    {
        var instruction =
            $"It is now your turn as {active.Name}. Write exactly one natural in-character reply only. Do not write for {counterpart.Name}. Never add your name, a speaker label, a heading, brackets around your name, or a narrator label. Do not end the scene. Keep the same language required by the LANGUAGE LOCK above.";
        if (!(scene.TurnState?.AdvanceAndAvoidRepetition ?? true)) return instruction;

        var recentTurns = scene.Messages
            .Where(message => message.Kind != ConversationMessageKind.DirectorEvent && !string.IsNullOrWhiteSpace(CurrentContent(message)))
            .OrderByDescending(message => message.SequenceNumber)
            .Take(5)
            .Reverse()
            .Select(message => $"- {PromptRules.ShortForTurnGuard(CurrentContent(message))}");

        var recentText = string.Join("\n", recentTurns);
        return $"""
            {instruction}

            [MANDATORY PROGRESSION CHECK FOR THIS TURN]
            The excerpt below is the most recent dialogue. Do not repeat, paraphrase, answer with the same promise, or return to the same unresolved phrase from it.
            This reply must visibly change the scene in one concrete way: make a decision that is acted upon, notice or reveal new information, initiate a specific next action, change the relationship dynamic, or cause a believable external development. A vague joke, a repeated question, or another promise to act later is not enough.
            If a previous threat, plan, contest, escape, or topic has already been mentioned repeatedly, either resolve it now or replace it with a genuinely new immediate beat.
            Recent turns that are off limits for echoing:
            {recentText}
            """;
    }

    private static string BuildDirectLanguageLock(PromptBuildRequest request)
    {
        var recent = request.Conversation.Messages?
            .OrderByDescending(message => message.SequenceNumber)
            .Take(8)
            .Select(CurrentContent)
            .ToArray() ?? [];
        return ReplyLanguage.BuildLockFromPreference(
            request.Character.ReplyLanguage,
            request.UserMessage,
            request.Character.SystemPrompt,
            request.Character.Description,
            request.Character.Personality,
            request.Character.Scenario,
            request.Character.ExampleDialogue,
            request.Conversation.Context.InitialUserProfile,
            request.Conversation.Context.InitialRelationshipContext,
            request.Conversation.SummaryText,
            request.Preset?.PromptText,
            string.Join("\n", recent.Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private static string BuildSceneLanguageLock(ConversationSnapshot scene, SoulCharacter active, SoulCharacter counterpart)
    {
        var recent = scene.Messages?
            .OrderByDescending(message => message.SequenceNumber)
            .Take(10)
            .Select(CurrentContent)
            .ToArray() ?? [];
        // Active speaker's language setting wins for this turn.
        return ReplyLanguage.BuildLockFromPreference(
            active.ReplyLanguage,
            scene.Name,
            scene.Context.Scenario,
            scene.Context.Location,
            scene.Context.TimeContext,
            scene.Context.Mood,
            scene.Context.Goal,
            scene.Context.RelationshipContext,
            scene.SummaryText,
            active.SystemPrompt,
            active.Description,
            active.Personality,
            active.Scenario,
            counterpart.SystemPrompt,
            counterpart.Description,
            counterpart.Personality,
            counterpart.Scenario,
            string.Join("\n", recent.Where(text => !string.IsNullOrWhiteSpace(text))));
    }

    private static bool IsCharacterMessage(ConversationSnapshot conversation, ConversationMessageSnapshot message) =>
        conversation.FindParticipant(message.AuthorParticipantId)?.Kind == ConversationParticipantKind.Character;

    private static string CurrentContent(ConversationMessageSnapshot message) =>
        (message.Variants.FirstOrDefault(x => x.Id == message.SelectedVariantId) ?? message.Variants.FirstOrDefault())?.Content
        ?? message.Content;
}
