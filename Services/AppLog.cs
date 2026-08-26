using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SoulExe.Services;

public static class AppLog
{
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, DataPaths.PortableDataFolderName, "logs");
    private const long MaximumLogBytes = 5 * 1024 * 1024;
    private const int RetainedArchiveCount = 4;
    private static readonly object WriteGate = new();
    public static string LogFilePath => Path.Combine(DirectoryPath, "SoulExe.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (WriteGate)
            {
                Directory.CreateDirectory(DirectoryPath);
                RotateIfNeeded(DirectoryPath, MaximumLogBytes, RetainedArchiveCount);
                var entry = new StringBuilder()
                    .Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .AppendLine(message);
                if (exception is not null) entry.AppendLine(exception.ToString());
                File.AppendAllText(LogFilePath, entry.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never cause a second application failure.
        }
    }

    internal static void RotateIfNeeded(string directoryPath, long maximumBytes, int retainedArchiveCount)
    {
        var current = Path.Combine(directoryPath, "SoulExe.log");
        if (!File.Exists(current) || new FileInfo(current).Length < maximumBytes) return;
        var oldest = Path.Combine(directoryPath, $"SoulExe.{retainedArchiveCount}.log");
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = retainedArchiveCount - 1; index >= 1; index--)
        {
            var source = Path.Combine(directoryPath, $"SoulExe.{index}.log");
            if (File.Exists(source)) File.Move(source, Path.Combine(directoryPath, $"SoulExe.{index + 1}.log"));
        }
        File.Move(current, Path.Combine(directoryPath, "SoulExe.1.log"));
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
