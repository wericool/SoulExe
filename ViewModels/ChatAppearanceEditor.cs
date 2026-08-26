using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Applies palette tokens from the appearance picker ("Token|#hex").</summary>
public static class ChatAppearanceEditor
{
    public static bool TryApplyColorToken(ChatAppearanceSettings appearance, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('|', 2);
        if (parts.Length != 2 || !parts[1].StartsWith('#')) return false;
        switch (parts[0])
        {
            case "TextColor": appearance.TextColor = parts[1]; return true;
            case "ActionColor": appearance.ActionColor = parts[1]; return true;
            case "QuoteColor": appearance.QuoteColor = parts[1]; return true;
            case "CodeColor": appearance.CodeColor = parts[1]; return true;
            case "AssistantBubbleColor": appearance.AssistantBubbleColor = parts[1]; return true;
            case "UserBubbleColor": appearance.UserBubbleColor = parts[1]; return true;
            case "ChatBackgroundColor": appearance.ChatBackgroundColor = parts[1]; return true;
            default: return false;
        }
    }
}
