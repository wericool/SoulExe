namespace SoulExe.Services;

public static class MessagingTiming
{
    private const int MinReplySeconds = 3;
    private const int MaxReplySeconds = 120;

    public static TimeSpan RealisticReplyDelay(string? message, Random? random = null)
    {
        random ??= Random.Shared;
        var length = Math.Clamp(message?.Trim().Length ?? 0, 0, 4000);
        // Two typed characters now count as one timing unit. This makes a
        // natural phrase of up to roughly 20 characters behave like a former
        // 10-character message, while long messages still add reading time.
        var timingLength = length / 2d;
        var lengthSeconds = (int)Math.Round(Math.Sqrt(timingLength) * 2.1);
        var jitter = random.Next(0, 31);
        return TimeSpan.FromSeconds(Math.Clamp(MinReplySeconds + lengthSeconds + jitter, MinReplySeconds, MaxReplySeconds));
    }

    public static TimeSpan NextProactiveDelay(Random? random = null)
    {
        random ??= Random.Shared;
        return TimeSpan.FromMinutes(random.Next(20, (5 * 60) + 1));
    }
}
