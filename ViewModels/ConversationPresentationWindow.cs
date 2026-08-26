namespace SoulExe.ViewModels;

/// <summary>Calculates the bounded snapshot range displayed by a conversation transcript.</summary>
public static class ConversationPresentationWindow
{
    public const int PageSize = 60;

    public static int LatestStart(int count) => Math.Max(0, count - PageSize);

    public static int PreviousStart(int currentStart) => Math.Max(0, currentStart - PageSize);

    public static int StartContaining(int count, int currentStart, int itemIndex)
    {
        if (itemIndex < 0 || itemIndex >= count || (itemIndex >= currentStart && itemIndex < currentStart + PageSize))
            return currentStart;
        return Math.Max(0, itemIndex - PageSize / 2);
    }
}
