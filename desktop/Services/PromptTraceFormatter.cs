using System.Text.RegularExpressions;

namespace SoulExe.Services;

/// <summary>Produces privacy-preserving, machine-readable prompt diagnostics for local logs.</summary>
public static partial class PromptTraceFormatter
{
    public static string Format(PromptBuildResult result)
    {
        var blocks = result.Messages.Select((message, index) =>
            $"block={index};source={message.role};chars={message.content.Length};tokens={ConversationContextWindow.EstimateTokens(message.content)};hash={AppLog.Fingerprint(message.content)}");
        var diagnostics = result.Diagnostics.Select(diagnostic =>
        {
            var cause = diagnostic.Text.Contains("Активирован", StringComparison.OrdinalIgnoreCase) ? "included"
                : diagnostic.Text.Contains("обрезан", StringComparison.OrdinalIgnoreCase) ? "trimmed"
                : diagnostic.Text.Contains("не вош", StringComparison.OrdinalIgnoreCase) ? "excluded"
                : "info";
            var limit = TokenLimit().Match(diagnostic.Text);
            return $"category={diagnostic.Category};cause={cause};limit={(limit.Success ? limit.Groups[1].Value : "-")};detailHash={AppLog.Fingerprint(diagnostic.Text)}";
        });
        return $"blocks=[{string.Join('|', blocks)}] diagnostics=[{string.Join('|', diagnostics)}]";
    }

    [GeneratedRegex(@"(\d+)\s*токен", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenLimit();
}
