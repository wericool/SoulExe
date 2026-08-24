using System.IO;

namespace SoulExe.Services;

/// <summary>Copies user-selected media into the app data directories.</summary>
public static class LocalMediaStore
{
    public static string CopyAvatar(string sourcePath, Guid ownerId, string avatarDirectory, string? fileNamePrefix = null)
    {
        Directory.CreateDirectory(avatarDirectory);
        var extension = Path.GetExtension(sourcePath);
        var fileName = string.IsNullOrWhiteSpace(fileNamePrefix)
            ? $"{ownerId}{extension}"
            : $"{fileNamePrefix}{ownerId}{extension}";
        var target = Path.Combine(avatarDirectory, fileName);
        File.Copy(sourcePath, target, overwrite: true);
        return target;
    }

    public static async Task<string> ImportGgufAsync(string sourcePath, string modelDirectory, CancellationToken token = default)
    {
        var localDirectory = Path.Combine(modelDirectory, "manual");
        Directory.CreateDirectory(localDirectory);
        var destination = Path.Combine(localDirectory, Path.GetFileName(sourcePath));
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            await using var input = File.OpenRead(sourcePath);
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, token);
        }
        return destination;
    }
}
