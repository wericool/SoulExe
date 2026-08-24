namespace SoulExe.Services;

// Fixture-only shape: production preset definitions stay in SoulMemoryService.
public sealed record SoulMemoryPresetMode(string Id, string DisplayName, string Description, bool UpdatesIndex, bool UpdatesTopics, bool UpdatesDiary)
{
    public static SoulMemoryPresetMode From(string? id) => string.Equals(id, "full", StringComparison.OrdinalIgnoreCase)
        ? new("full", "Full", "Router + Archivist + Diary", true, true, true)
        : new("index", "Index only", "Router only", true, false, false);
}
