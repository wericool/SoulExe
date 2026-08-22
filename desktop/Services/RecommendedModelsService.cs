using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SoulTextWpf.Services;

public sealed record RecommendedModel(
    string RepositoryId,
    string Name,
    string Author,
    string Description,
    string AuthorNotes,
    string OptimalQuant,
    int MinimumRamGb,
    int RecommendedRamGb,
    int MinimumVramGb,
    int RecommendedVramGb,
    long Downloads,
    long Likes)
{
    public string HardwareSummary => RecommendedVramGb > 0
        ? $"Нужно примерно: RAM от {MinimumRamGb} ГБ (лучше {RecommendedRamGb} ГБ), видеокарта ~{RecommendedVramGb} ГБ VRAM"
        : $"Нужно примерно: RAM от {MinimumRamGb} ГБ (лучше {RecommendedRamGb} ГБ); можно на CPU без дискретной GPU";
    public string QuantSummary => $"Рекомендуемый квант: {OptimalQuant} (баланс размера файла и качества)";
    public string BeginnerHint => RecommendedVramGb >= 24
        ? "Крупная модель: имеет смысл при мощной GPU (много VRAM)."
        : RecommendedVramGb >= 12
            ? "Средняя/крупная модель: комфортнее на видеокарте от ~12 ГБ VRAM."
            : RecommendedVramGb > 0
                ? "Относительно лёгкая для GPU: подходит многим современным картам."
                : "Хороший кандидат для слабого ПК или режима только на CPU.";
    public string? InstalledFileName { get; init; }
    public bool IsInstalled => !string.IsNullOrWhiteSpace(InstalledFileName);
    public string InstallationStateText => IsInstalled ? $"Уже скачана: {InstalledFileName}" : "Не скачана";
    public string RepositoryUrl => $"https://huggingface.co/{RepositoryId}";
    public string EstimatedFileSize
    {
        get
        {
            var match = Regex.Match($"{Name} {RepositoryId}", @"(?<![\d.])(\d+(?:[.,]\d+)?)\s*B\b", RegexOptions.IgnoreCase);
            if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var billions))
            {
                var sizeGb = Math.Max(1d, Math.Round(billions * 0.58d, 1));
                return $"≈ {sizeGb:0.#} ГБ GGUF";
            }
            return "≈ размер GGUF уточняется";
        }
    }
}

/// <summary>
/// Uses the same curated list and 24-hour portable cache as the original Soul-of-Waifu Models Hub.
/// The embedded fallback leaves a useful list available when the PC starts offline for the first time.
/// </summary>
public sealed class RecommendedModelsService : IDisposable
{
    private const string CuratedModelsUrl = "https://raw.githubusercontent.com/jofizcd/sow-data/main/recommended_models.json";
    private readonly string _cachePath;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public RecommendedModelsService(DataPaths? paths = null)
    {
        var p = paths ?? AppServices.Paths;
        _cachePath = Path.Combine(p.Root, "recommended_models_cache.json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoulExe/0.4 (local Windows application)");
    }

    public async Task<IReadOnlyList<RecommendedModel>> GetAsync(bool forceRefresh = false, CancellationToken token = default)
    {
        string? json = null;
        var cacheFresh = File.Exists(_cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(_cachePath) < TimeSpan.FromHours(24);
        if (!forceRefresh && cacheFresh) json = await File.ReadAllTextAsync(_cachePath, token);
        if (json is null)
        {
            try
            {
                json = await _http.GetStringAsync(CuratedModelsUrl, token);
                await File.WriteAllTextAsync(_cachePath, json, token);
            }
            catch
            {
                if (File.Exists(_cachePath)) json = await File.ReadAllTextAsync(_cachePath, token);
            }
        }

        var models = string.IsNullOrWhiteSpace(json) ? [] : Parse(json);
        // Внешний каталог и кеш могут обновляться позднее самой программы. Объединяем их со
        // встроенным списком, поэтому новые рекомендации из SoulExe (в том числе Runeweaver)
        // не исчезают до обновления удалённого источника или его кеша.
        return MergeWithEmbeddedCatalog(models);
    }

    private IReadOnlyList<RecommendedModel> MergeWithEmbeddedCatalog(IReadOnlyList<RecommendedModel> remoteModels)
    {
        var merged = remoteModels.ToList();
        foreach (var embedded in LoadEmbeddedCatalog())
        {
            if (merged.Any(x => string.Equals(x.RepositoryId, embedded.RepositoryId, StringComparison.OrdinalIgnoreCase)))
                continue;
            merged.Add(embedded);
        }

        return merged
            .OrderBy(x => string.Equals(x.RepositoryId, "mradermacher/MN-12B-Runeweaver-RP-RU-i1-GGUF", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.RecommendedRamGb)
            .ThenByDescending(x => x.Downloads)
            .ToList();
    }

    private List<RecommendedModel> Parse(string json)
    {
        var root = JsonSerializer.Deserialize<CuratedRoot>(json, _json);
        return root?.Models?
            .Where(x => !string.IsNullOrWhiteSpace(x.HfId))
            .Select(x => new RecommendedModel(
                x.HfId!, x.Name ?? x.HfId!, x.Author ?? "Unknown",
                string.IsNullOrWhiteSpace(x.DescriptionRu) ? x.DescriptionEn ?? "Описание отсутствует." : x.DescriptionRu,
                x.AuthorNotes ?? "", string.IsNullOrWhiteSpace(x.OptimalQuant) ? "Q4_K_M" : x.OptimalQuant,
                x.MinRamGb, x.RecommendedRamGb, x.MinVramGb, x.RecommendedVramGb,
                x.Downloads, x.Likes))
            .OrderBy(x => string.Equals(x.RepositoryId, "mradermacher/MN-12B-Runeweaver-RP-RU-i1-GGUF", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(x => x.RecommendedRamGb)
            .ThenByDescending(x => x.Downloads)
            .ToList() ?? [];
    }

    public IReadOnlyList<RecommendedModel> GetEmbeddedCatalog() => LoadEmbeddedCatalog();

    private IReadOnlyList<RecommendedModel> LoadEmbeddedCatalog()
    {
        const string resourceName = "SoulTextWpf.Assets.recommended_models.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null) return BuiltInEmergencyFallback;
        using var reader = new StreamReader(stream);
        var models = Parse(reader.ReadToEnd());
        return models.Count > 0 ? models : BuiltInEmergencyFallback;
    }

    private static readonly IReadOnlyList<RecommendedModel> BuiltInEmergencyFallback =
    [
        new("HauhauCS/Gemma-4-E2B-Uncensored-HauhauCS-Aggressive", "Gemma-4-E2B-Uncensored-Aggressive", "HauhauCS", "Сверхлёгкая модель для простого casual RP на слабом железе.", "Температура 0.85–1.1.", "Q4_K_P", 4, 8, 0, 4, 173129, 207)
    ];

    public void Dispose() => _http.Dispose();

    private sealed class CuratedRoot { public List<CuratedModel>? Models { get; set; } }
    private sealed class CuratedModel
    {
        [JsonPropertyName("hf_id")] public string? HfId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("downloads")] public long Downloads { get; set; }
        [JsonPropertyName("likes")] public long Likes { get; set; }
        [JsonPropertyName("min_ram_gb")] public int MinRamGb { get; set; }
        [JsonPropertyName("recommended_ram_gb")] public int RecommendedRamGb { get; set; }
        [JsonPropertyName("min_vram_gb")] public int MinVramGb { get; set; }
        [JsonPropertyName("recommended_vram_gb")] public int RecommendedVramGb { get; set; }
        [JsonPropertyName("optimal_quant")] public string? OptimalQuant { get; set; }
        [JsonPropertyName("description_en")] public string? DescriptionEn { get; set; }
        [JsonPropertyName("description_ru")] public string? DescriptionRu { get; set; }
        [JsonPropertyName("author_notes")] public string? AuthorNotes { get; set; }
    }
}
