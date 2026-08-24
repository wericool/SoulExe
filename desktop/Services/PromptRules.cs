using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Shared prompt fragments and helpers used by both direct chats and two-character scenes.
/// Keeps wording in one place so Direct and Scene policies cannot drift apart.
/// </summary>
public static class PromptRules
{
    public const string DirectResponseMode = """
[DIRECT RESPONSE MODE]
Answer directly and concisely. Do not produce internal chain-of-thought, <think> blocks, hidden reasoning, or analysis before the answer. Preserve the requested style and answer only with the final response.
""";

    /// <summary>Applies semantic generation policy before the request reaches the LLM transport.</summary>
    public static IReadOnlyList<LlamaMessage> WithDirectResponseMode(IReadOnlyList<LlamaMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var result = new List<LlamaMessage>(messages.Count + 1) { new("system", DirectResponseMode) };
        result.AddRange(messages);
        return result;
    }

    public static string PersonalityExpressionRule(string? level) => (level ?? "natural").Trim().ToLowerInvariant() switch
    {
        "vivid" => "[PERSONALITY EXPRESSION] Make the listed personality traits clearly noticeable in many appropriate reactions, but do not repeat labels or explain them mechanically.",
        "subtle" => "[PERSONALITY EXPRESSION] Keep the listed traits as a quiet background. Reveal them only when the situation naturally invites it; do not name or emphasize them without a reason.",
        _ => "[PERSONALITY EXPRESSION] Express the listed traits naturally and situationally. Do not mention, repeat, or force a trait into every reply; allow varied emotions and reactions."
    };

    private const string SharedRoleplayFormat = """
[RESPONSE FORMAT — READABLE ROLEPLAY]
Keep spoken words as plain text; quotation marks are optional. Do not manually add blank lines or separate paragraphs merely to distinguish actions, thoughts and speech: SoulExe formats the completed reply locally.
Do not expose chain-of-thought, private analysis, a narrator label or <think> blocks.
""";

    /// <summary>Shared layout policy with only the mode-specific author boundary appended.</summary>
    public static string RoleplayFormat(ConversationKind kind) => kind == ConversationKind.Scene
        ? SharedRoleplayFormat + """
Write no name or speaker label. Use single asterisks for actions, gestures, visible reactions, brief scene descriptions and, when useful, one brief in-character thought such as *Мысль: ...*. Do not use repetitive novelistic attribution such as “Name said” after every sentence.
Never write the other participant's undeclared actions, words, thoughts, choices or feelings for them.
"""
        : SharedRoleplayFormat + """
Use single asterisks only for narrative actions, gestures, scene descriptions and non-verbal reactions: *She steps closer and tilts her head.*
Never write the user's undeclared actions, words, thoughts, choices or feelings for them.
""";

    public const string ContinuationDirectorCommand = """
[DIRECTOR CONTINUATION COMMAND]
*continue*
This is an internal application-level director command, not a message, action, thought, question, request, or established fact from the user.
Do not quote, discuss, acknowledge, or ask about this command. Continue the current situation naturally from the previous character reply. The previous assistant entry may contain several consecutive parts of the same character's monologue merged by the application; treat all of them as already spoken by the character. Write exactly one fresh in-character reply only; do not write for the user and do not wait for a new user message.
""";

    public const string SceneContract = """
[SCENE CONTRACT]
This is an ongoing two-character scene. Write only as the current speaker. Never narrate, speak, decide, think, feel or act for the other participant. Preserve the established location, time, tone, relationship and confirmed events. Do not jump to another place or time, conclude the scene, break character, add a narrator, or expose hidden reasoning unless a DIRECTOR EVENT explicitly changes the scene.
""";

    public const string SceneProgressionRules = """
[SCENE PROGRESSION — AVOID LOOPS]
Before replying, consider the last several scene turns. Do not repeat, summarize, echo, or merely paraphrase what has already been said. Every reply must add one meaningful new beat that fits the established scene: a fresh observation, reaction, question, decision, small action, emotional shift, detail, or a natural follow-up. Move the current goal forward in small believable steps. If the current topic is exhausted, transition naturally to a related topic or action while preserving the scene's place, time, relationship, and tone. Do not force a dramatic jump and do not conclude the scene.
""";

    public static List<LlamaMessage> CollapseConsecutiveAssistantTurns(IEnumerable<LlamaMessage> messages)
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

    public static string TrimToTokenBudget(string content, int budget) =>
        content.Length <= budget * 4 ? content : content[..Math.Min(content.Length, budget * 4)] + "…";

    public static string ShortForTurnGuard(string content)
    {
        var compact = string.Join(" ", content.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 260 ? compact : compact[..260].TrimEnd() + "…";
    }
}
