using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Shared, deliberately conservative context-window calculations for every conversation mode.
/// Policies decide which system sections to add; this helper only decides how much recent history fits.
/// </summary>
public static class ConversationContextWindow
{
    public static int EstimateTokens(string? value) => Math.Max(1, ((value ?? string.Empty).Length + 1) / 2);

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

/// <summary>Mode-specific turn scheduling rules, kept independent of Windows or network UI.</summary>
public static class ConversationTurnPolicy
{
    public static bool CanScheduleAutomaticTurn(string? status, string? turnMode, int delaySeconds) =>
        string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)
        && string.Equals(turnMode, "alternate", StringComparison.OrdinalIgnoreCase)
        && delaySeconds >= 5;

    public static DateTimeOffset? NextTurnAt(string? status, string? turnMode, int delaySeconds, DateTimeOffset now) =>
        CanScheduleAutomaticTurn(status, turnMode, delaySeconds)
            ? now.AddSeconds(Math.Clamp(delaySeconds, 5, 30))
            : null;

    public static string NextStatusAfterGeneratedTurn(string? turnMode) =>
        string.Equals(turnMode, "alternate", StringComparison.OrdinalIgnoreCase) ? "running" : "paused";
}
