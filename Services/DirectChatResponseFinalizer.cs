using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Shared post-generation pass for direct chats: extract state variables, strip control blocks,
/// apply roleplay layout. Used by desktop UI and network transports.
/// </summary>
public static class DirectChatResponseFinalizer
{
    public static async Task<string> FinalizeAsync(
        StateVariableService stateVariables,
        Guid characterId,
        Guid chatId,
        string rawResponse,
        bool useRoleplayFormatting,
        CancellationToken token = default)
    {
        var raw = rawResponse ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            return "Модель не вернула текст.";

        await stateVariables.ApplyFromResponseAsync(characterId, chatId, raw, token);
        var cleaned = stateVariables.RemoveStateBlocks(raw);
        var formatted = ResponseFormatter.NormalizeRoleplayLayout(cleaned, useRoleplayFormatting);
        return string.IsNullOrWhiteSpace(formatted) ? "Модель не вернула текст." : formatted.Trim();
    }
}
