namespace SoulExe.Models;

/// <summary>Shared cognitive background mode constants.</summary>
internal static class BackgroundModes
{
    public const string Idle = "idle";
    public const string Immediate = "immediate";
    public const string Manual = "manual";

    public static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        Immediate => Immediate,
        Manual => Manual,
        _ => Idle
    };
}
