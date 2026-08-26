using System.IO;

namespace SoulExe.Services;

/// <summary>Sanitizes user-facing names for use in file dialogs.</summary>
public static class SafeFileNames
{
    public static string ForExport(string? name, string fallback = "character")
    {
        var cleaned = string.Concat((name ?? "").Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
    }
}
