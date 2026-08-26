using System.IO;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Matches a recommended GGUF entry to an already installed local model file.</summary>
public static class RecommendedModelMatcher
{
    public static bool IsMatch(RecommendedModel recommendation, SoulModelInstallation local)
    {
        var localFile = Path.GetFileName(local.LocalPath);
        if (string.IsNullOrWhiteSpace(localFile)) return false;
        if (!localFile.Contains(recommendation.OptimalQuant, StringComparison.OrdinalIgnoreCase)) return false;
        if (local.Metadata.TryGetValue("repository_id", out var repository) &&
            string.Equals(repository, recommendation.RepositoryId, StringComparison.OrdinalIgnoreCase))
            return true;

        var repoTail = recommendation.RepositoryId.Split('/').LastOrDefault() ?? recommendation.RepositoryId;
        var normalizedRepo = NormalizeFileToken(repoTail);
        var normalizedFile = NormalizeFileToken(localFile);
        return normalizedRepo.Length >= 6
               && (normalizedFile.Contains(normalizedRepo, StringComparison.Ordinal)
                   || normalizedRepo.Contains(normalizedFile[..Math.Min(normalizedFile.Length, normalizedRepo.Length)], StringComparison.Ordinal));
    }

    public static string NormalizeFileToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
