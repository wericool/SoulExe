using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Shared, deliberately conservative context-window calculations for every conversation mode.
/// Policies decide which system sections to add; this helper only decides how much recent history fits.
/// </summary>
public static class ConversationContextWindow
{
    public static int EstimateTokens(string? value) => Math.Max(1, (value ?? string.Empty).Length * 2 / 3);

    public static int CalculateHistoryBudget(int contextSize, string systemContext, int reservedGenerationTokens, int safetyReserve)
    {
        var contextLimit = Math.Max(1024, contextSize);
        var reservedGeneration = Math.Clamp(reservedGenerationTokens, 64, Math.Max(64, contextLimit - 768));
        return Math.Max(256, contextLimit - EstimateTokens(systemContext) - reservedGeneration - Math.Max(0, safetyReserve));
    }

    public static IReadOnlyList<T> TakeLatestThatFits<T>(IReadOnlyList<T> history, int budget, Func<T, string> content, Action? onTrimmed = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(content);
        var accepted = new List<T>();
        var spent = 0;
        for (var index = history.Count - 1; index >= 0; index--)
        {
            var item = history[index];
            var cost = EstimateTokens(content(item));
            if (spent + cost > budget)
            {
                onTrimmed?.Invoke();
                break;
            }
            accepted.Add(item);
            spent += cost;
        }
        accepted.Reverse();
        return accepted;
    }
}

/// <summary>
/// Stable allocation for optional context. Every allocation comes from one finite remainder,
/// so optional blocks cannot collectively evict the protected recent history.
/// </summary>
public sealed record ContextBudgetPlan(int InputBudget, int ReservedHistoryTokens, int BaseContextTokens, int LoreTokens, int SummaryTokens, int MemoryTokens, int CharacterTokens, int StateTokens)
{
    public static ContextBudgetPlan Create(int contextSize, int reservedGenerationTokens, int safetyReserve, int newestMessageTokens = 0)
    {
        var contextLimit = Math.Max(1024, contextSize);
        var reservedGeneration = Math.Clamp(reservedGenerationTokens, 64, Math.Max(64, contextLimit - 768));
        var inputBudget = Math.Max(256, contextLimit - reservedGeneration - Math.Max(0, safetyReserve) - Math.Max(0, newestMessageTokens));
        var historyReserve = Math.Clamp(inputBudget * 35 / 100, Math.Min(128, inputBudget), Math.Max(128, inputBudget - 128));
        var optionalContext = Math.Max(0, inputBudget - historyReserve);
        var baseContext = Math.Min(optionalContext, Math.Clamp(optionalContext * 20 / 100, 192, 900));
        var remaining = optionalContext - baseContext;
        var character = Math.Min(remaining, Math.Clamp(remaining * 40 / 100, 128, 1600));
        remaining -= character;
        var state = Math.Min(remaining, Math.Clamp(remaining * 20 / 100, 64, 512));
        remaining -= state;
        var lore = Math.Min(remaining, Math.Clamp(remaining * 40 / 100, 64, 1024));
        remaining -= lore;
        var summary = Math.Min(remaining, Math.Clamp(remaining * 50 / 100, 64, 1024));
        remaining -= summary;
        var memory = Math.Min(remaining, 1200);
        return new ContextBudgetPlan(inputBudget, historyReserve, baseContext, lore, summary, memory, character, state);
    }
}

/// <summary>Mode-specific turn scheduling rules, kept independent of Windows or network UI.</summary>
public static class ConversationTurnPolicy
{
    public static bool CanScheduleAutomaticTurn(string? status, string? turnMode, int delaySeconds) =>
        string.Equals(status, SceneStatus.Running, StringComparison.OrdinalIgnoreCase)
        && string.Equals(turnMode, "alternate", StringComparison.OrdinalIgnoreCase)
        && delaySeconds >= 5;

    public static DateTimeOffset? NextTurnAt(string? status, string? turnMode, int delaySeconds, DateTimeOffset now) =>
        CanScheduleAutomaticTurn(status, turnMode, delaySeconds)
            ? now.AddSeconds(Math.Clamp(delaySeconds, 5, 30))
            : null;

    public static string NextStatusAfterGeneratedTurn(string? turnMode) =>
        string.Equals(turnMode, "alternate", StringComparison.OrdinalIgnoreCase) ? SceneStatus.Running : SceneStatus.Paused;
}
