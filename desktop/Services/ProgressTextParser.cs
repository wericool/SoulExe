namespace SoulExe.Services;

/// <summary>Extracts a percentage marker from installer / download status strings.</summary>
public static class ProgressTextParser
{
    public static bool TryReadPercent(string message, out double percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(message)) return false;
        var marker = message.LastIndexOf('%');
        if (marker <= 0) return false;
        var start = marker - 1;
        while (start >= 0 && (char.IsDigit(message[start]) || message[start] is '.' or ','))
            start--;
        var token = message[(start + 1)..marker].Replace(',', '.');
        return double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent);
    }
}
