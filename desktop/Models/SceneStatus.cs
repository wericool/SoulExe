namespace SoulExe.Models;

/// <summary>Shared scene status constants used across ViewModels, Services, and NetworkChatServer.</summary>
internal static class SceneStatus
{
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Finished = "finished";

    public static string DisplayFor(string? status) =>
        status == Running ? "Идёт" :
        status == Finished ? "Завершена" :
        "Пауза";
}
