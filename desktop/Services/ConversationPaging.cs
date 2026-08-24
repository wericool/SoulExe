using System;
using System.Text;
using SoulExe.Models;

namespace SoulExe.Services;

public sealed record ConversationPageCursor(DateTime UpdatedAt, Guid Id);

/// <summary>Shared, deterministic pagination rules for the conversation HTTP API.</summary>
public static class ConversationPaging
{
    public static int? ReadMessageTake(string? rawValue) =>
        int.TryParse(rawValue, out var value) && value > 0
            ? Math.Clamp(value, 1, 100)
            : null;

    public static int ReadPageSize(string? rawValue) =>
        int.TryParse(rawValue, out var value) && value > 0
            ? Math.Clamp(value, 1, 100)
            : 50;

    public static ConversationPageCursor? ParseCursor(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return null;
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var values = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|', 2);
            return values.Length == 2 && long.TryParse(values[0], out var ticks) && Guid.TryParse(values[1], out var id)
                ? new ConversationPageCursor(new DateTime(ticks, DateTimeKind.Utc), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string CreateCursor(ConversationSnapshot conversation) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{conversation.UpdatedAt.Ticks}|{conversation.Id}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
