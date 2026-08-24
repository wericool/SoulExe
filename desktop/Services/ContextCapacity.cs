namespace SoulExe.Services;

/// <summary>Detects llama.cpp context-window overflow errors across transports.</summary>
public static class ContextCapacity
{
    public static bool IsOverflow(Exception exception)
    {
        var text = exception.ToString();
        return text.Contains("exceed_context_size_error", StringComparison.OrdinalIgnoreCase)
               || text.Contains("exceeds the available context size", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatUserMessage(string context) =>
        $"{context}: контекст модели переполнен. SoulExe безопасно остановил текущую операцию; новая версия сокращает старую историю автоматически.";
}
