using System.IO;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Enriches recommended-model rows with local installation markers.</summary>
public static class RecommendedCatalog
{
    public static RecommendedModel WithInstallationState(
        RecommendedModel model,
        IEnumerable<SoulModelInstallation> installedModels)
    {
        var installed = installedModels.FirstOrDefault(local => RecommendedModelMatcher.IsMatch(model, local));
        return model with
        {
            InstalledFileName = installed is null ? null : Path.GetFileName(installed.LocalPath)
        };
    }

    public static IReadOnlyList<RecommendedModel> MarkInstalled(
        IEnumerable<RecommendedModel> models,
        IEnumerable<SoulModelInstallation> installedModels) =>
        models.Select(model => WithInstallationState(model, installedModels)).ToList();
}
