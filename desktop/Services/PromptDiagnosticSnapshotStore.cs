namespace SoulExe.Services;

public sealed record PromptDiagnosticSnapshot(string GenerationId, DateTimeOffset CreatedAt, string Trace);

/// <summary>Keeps only the latest privacy-preserving prompt structure; message text is never stored.</summary>
public static class PromptDiagnosticSnapshotStore
{
    private static readonly object Gate = new();
    private static PromptDiagnosticSnapshot? _latest;

    public static PromptDiagnosticSnapshot Publish(string generationId, PromptBuildResult result)
    {
        var snapshot = new PromptDiagnosticSnapshot(generationId, DateTimeOffset.UtcNow, PromptTraceFormatter.Format(result));
        lock (Gate) _latest = snapshot;
        return snapshot;
    }

    public static PromptDiagnosticSnapshot? Latest()
    {
        lock (Gate) return _latest;
    }
}
