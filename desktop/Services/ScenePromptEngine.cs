using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class ScenePromptEngine
{
    public PromptBuildResult Build(SceneRuntime runtime, Guid activeCharacterId, int contextSize, int reservedGenerationTokens)
    {
        var scene = runtime.Scene;
        var active = activeCharacterId == runtime.First.Id ? runtime.First : runtime.Second;
        var counterpart = active.Id == runtime.First.Id ? runtime.Second : runtime.First;
        var systems = new List<string>();
        if (scene.EnforceSceneContract) systems.Add(SceneContract);
        if (scene.AdvanceSceneAndAvoidRepetition) systems.Add(SceneProgressionRules);
        systems.Add(BuildSceneState(scene));
        systems.Add(BuildCharacter("YOU — CURRENT SPEAKER", active));
        systems.Add(BuildCounterpart(counterpart));
        if (active.UseRoleplayResponseFormatting) systems.Add(SceneRoleplayResponseFormattingRules);
        var lore = BuildLore(active, runtime.Lorebooks);
        if (!string.IsNullOrWhiteSpace(lore)) systems.Add(lore);
        if (!string.IsNullOrWhiteSpace(scene.SummaryText)) systems.Add($"[SHARED SCENE SUMMARY]\n{scene.SummaryText.Trim()}");
        var result = new List<LlamaMessage> { new("system", string.Join("\n\n", systems.Where(text => !string.IsNullOrWhiteSpace(text)))) };
        // The local estimate is deliberately conservative. A small reserve prevents llama.cpp
        // from rejecting a scene turn when real tokenizer output is a few tokens above it.
        var contextLimit = Math.Max(1024, contextSize);
        var reservedGeneration = Math.Clamp(reservedGenerationTokens, 64, Math.Max(64, contextLimit - 768));
        var tokenizerSafetyReserve = Math.Max(768, Math.Min(1536, Math.Max(256, reservedGeneration / 2)));
        var tokenBudget = Math.Max(256, contextLimit - EstimateTokens(result[0].content) - reservedGeneration - tokenizerSafetyReserve);
        var history = new List<LlamaMessage>();
        foreach (var message in scene.Messages.OrderBy(message => message.SequenceNumber))
        {
            var role = message.Kind == SoulSceneMessageKind.Director ? "system" : message.SpeakerCharacterId == active.Id ? "assistant" : "user";
            // Roles already indicate whether this is the active speaker or the counterpart.
            // Do not add visual name labels here: small local models tend to copy them into their next reply.
            var prefix = message.Kind == SoulSceneMessageKind.Director ? "[DIRECTOR EVENT] " : "";
            history.Add(new LlamaMessage(role, prefix + message.Content));
        }
        var accepted = new List<LlamaMessage>();
        var spent = 0;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var cost = EstimateTokens(history[index].content);
            if (spent + cost > tokenBudget) break;
            accepted.Add(history[index]); spent += cost;
        }
        accepted.Reverse(); result.AddRange(accepted);
        result.Add(new LlamaMessage("user", BuildTurnInstruction(scene, active, counterpart)));
        return new PromptBuildResult(result, []);
    }

    private static string BuildTurnInstruction(SoulScene scene, SoulCharacter active, SoulCharacter counterpart)
    {
        var instruction = $"It is now your turn as {active.Name}. Write exactly one natural in-character reply only. Do not write for {counterpart.Name}. Never add your name, a speaker label, a heading, brackets around your name, or a narrator label. Do not end the scene.";
        if (!scene.AdvanceSceneAndAvoidRepetition) return instruction;

        var recentTurns = scene.Messages
            .Where(message => message.Kind != SoulSceneMessageKind.Director && !string.IsNullOrWhiteSpace(message.Content))
            .OrderByDescending(message => message.SequenceNumber)
            .Take(5)
            .Reverse()
            .Select(message => $"- {ShortForTurnGuard(message.Content)}");

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

    private static string ShortForTurnGuard(string content)
    {
        var compact = string.Join(" ", content.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 260 ? compact : compact[..260].TrimEnd() + "…";
    }

    private static string BuildSceneState(SoulScene scene) => $"""
        [SCENE]
        Scenario: {scene.Scenario}
        Location: {scene.Location}
        Time: {scene.TimeContext}
        Mood: {scene.Mood}
        Current goal: {scene.Goal}
        Shared relationship / established context: {scene.RelationshipContext}
        """;

    private static string BuildCharacter(string title, SoulCharacter character) => $"""
        [{title}: {character.Name}]
        Title: {character.Title}
        Description: {character.Description}
        Personality: {character.Personality}
        Character instructions: {character.SystemPrompt}
        """;

    private static string BuildCounterpart(SoulCharacter character) => $"""
        [COUNTERPART: {character.Name}]
        Description: {character.Description}
        Personality: {character.Personality}
        Treat this as the other participant. Never write, choose, think, feel or act for them.
        """;

    private static string BuildLore(SoulCharacter character, IReadOnlyDictionary<Guid, SoulLorebook> all)
    {
        var entries = character.LorebookIds.Where(all.ContainsKey).SelectMany(id => all[id].Entries)
            .Where(entry => entry.IsEnabled && (entry.TriggerMode is "always" or "constant" || string.IsNullOrWhiteSpace(entry.TriggerMode))).Take(8).ToList();
        if (entries.Count == 0) return "";
        var text = new StringBuilder("[ACTIVE CHARACTER LORE]");
        foreach (var entry in entries) text.Append($"\n\n[{entry.Name}]\n{entry.Content}");
        return text.ToString();
    }

    private const string SceneRoleplayResponseFormattingRules = """
        [SCENE RESPONSE FORMAT — READABLE ROLEPLAY]
        Write no name or speaker label. Use single asterisks for actions, gestures, visible reactions, brief scene descriptions and, when useful, one brief in-character thought such as *Мысль: ...*.
        Keep spoken words as plain text. Do not manually add blank lines or separate paragraphs merely to distinguish types of text: SoulExe formats the completed reply locally.
        Never expose chain-of-thought, private analysis or a narrator label. Do not use <think> blocks or repetitive novelistic attribution such as “Name said” after every sentence.
        """;

    private const string SceneContract = """
        [SCENE CONTRACT]
        This is an ongoing two-character scene. Write only as the current speaker. Never narrate, speak, decide, think, feel or act for the other participant. Preserve the established location, time, tone, relationship and confirmed events. Do not jump to another place or time, conclude the scene, break character, add a narrator, or expose hidden reasoning unless a DIRECTOR EVENT explicitly changes the scene.
        """;

    private const string SceneProgressionRules = """
        [SCENE PROGRESSION — AVOID LOOPS]
        Before replying, consider the last several scene turns. Do not repeat, summarize, echo, or merely paraphrase what has already been said. Every reply must add one meaningful new beat that fits the established scene: a fresh observation, reaction, question, decision, small action, emotional shift, detail, or a natural follow-up. Move the current goal forward in small believable steps. If the current topic is exhausted, transition naturally to a related topic or action while preserving the scene's place, time, relationship, and tone. Do not force a dramatic jump and do not conclude the scene.
        """;

    private static int EstimateTokens(string value) => Math.Max(1, ((value ?? "").Length + 1) / 2);
}

public static class SceneResponseFormatter
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

    private static void AddPlainParagraph(ICollection<string> paragraphs, string source)
    {
        var plain = source.Trim();
        if (!string.IsNullOrWhiteSpace(plain)) paragraphs.Add(plain);
    }
}
