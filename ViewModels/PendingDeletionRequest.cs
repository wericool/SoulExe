namespace SoulExe.ViewModels;

/// <summary>Transient UI-only deletion intent; no destructive operation runs before confirmation.</summary>
public sealed class PendingDeletionRequest
{
    public PendingDeletionRequest(string title, string description, string warning, Func<Task> execute)
    {
        Title = title;
        Description = description;
        Warning = warning;
        Execute = execute;
    }

    public string Title { get; }
    public string Description { get; }
    public string Warning { get; }
    internal Func<Task> Execute { get; }
}
