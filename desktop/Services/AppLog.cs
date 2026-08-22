using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SoulTextWpf.Services;

public static class AppLog
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, DataPaths.PortableDataFolderName, "logs");
    public static string LogFilePath => Path.Combine(DirectoryPath, "SoulExe.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var entry = new StringBuilder()
                .Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .AppendLine(message);
            if (exception is not null) entry.AppendLine(exception.ToString());
            File.AppendAllText(LogFilePath, entry.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Logging must never cause a second application failure.
        }
    }

    public static string Fingerprint(string? value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes)[..16];
    }

    public static string NormalizeForComparison(string? value) => string.Join(' ',
        (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();

    public static string Preview(string? value, int maximumLength = 240)
    {
        var text = NormalizeForComparison(value);
        return text.Length <= maximumLength ? text : text[..maximumLength] + "…";
    }
}
