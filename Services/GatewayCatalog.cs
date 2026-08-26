using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Merges gateway search pages and marks already-installed lorebooks.</summary>
public static class GatewayCatalog
{
    public static int MergePage(
        IList<GatewayAssetItem> target,
        IEnumerable<GatewayAssetItem> page,
        IEnumerable<string> installedLoreNames,
        out bool hasMoreChubPage)
    {
        var loreNames = installedLoreNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var known = target.Select(item => $"{item.Kind}:{item.Id}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var pageList = page.ToList();
        foreach (var result in pageList.Where(item => known.Add($"{item.Kind}:{item.Id}")))
        {
            if (result.Kind == "lorebook")
                result.IsAlreadyImported = loreNames.Contains(result.Name);
            target.Add(result);
            added++;
        }
        hasMoreChubPage = pageList.Count >= 30 && added > 0;
        return added;
    }

    public static string StatusLine(string categoryTitle, int totalCount, bool hasMore) =>
        totalCount == 0
            ? $"В категории «{categoryTitle}» ничего не найдено."
            : $"{categoryTitle}: загружено {totalCount} материалов{(hasMore ? ". Можно загрузить ещё." : ".")}";
}
