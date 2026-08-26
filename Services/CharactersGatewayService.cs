using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

public sealed record GatewayAssetItem(
    string Kind,
    string Id,
    string Name,
    string Author,
    string Description,
    string AvatarUrl,
    string DownloadUrl,
    int Likes,
    int Downloads,
    int TokenCount,
    int EntryCount,
    string Location)
{
    public bool IsCharacter => Kind is "soul" or "chub";
    public string TypeLabel => Kind switch
    {
        "soul" => "SOUL GATEWAY · CHARACTER CARD V2",
        "chub" => "CHUB AI · CHARACTER",
        "lorebook" => "WORLD LOREBOOK",
        "scenario" => "TEXT SCENARIO",
        _ => "GATEWAY ASSET"
    };

    /// <summary>True when this lorebook (or matching asset) is already in the local library.</summary>
    public bool IsAlreadyImported { get; set; }

    public string ImportLabel => IsAlreadyImported
        ? "Уже установлено"
        : Kind switch
        {
            "soul" or "chub" => "Импортировать персонажа",
            "lorebook" => "Импортировать лорбук",
            "scenario" => "Создать чат по сценарию",
            _ => "Импортировать"
        };

    public string MetaLine => Kind switch
    {
        "chub" => $"♥ {Likes:N0}    ◷ {Downloads:N0}    ◈ {TokenCount:N0}",
        "lorebook" => $"Автор: {Author}    •    Записей: {EntryCount}",
        "scenario" => $"Автор: {Author}    •    Старт: {Location}",
        _ => $"Автор: {Author}"
    };
}

public sealed record GatewayCharacterDetails(string FullPath, string Name, string Tagline, string AvatarUrl, string Description, string Personality, string Scenario, string FirstMessage, string ExampleDialogues, IReadOnlyList<string> AlternateGreetings);

public sealed class CharactersGatewayService
{
    private const string SoulRegistryUrl = "https://raw.githubusercontent.com/jofizcd/sow-data/main/soul_registry.json";
    private const string LorebooksRegistryUrl = "https://raw.githubusercontent.com/jofizcd/sow-data/main/lorebooks_registry.json";
    private const string StagesRegistryUrl = "https://raw.githubusercontent.com/jofizcd/sow-data/main/stages_registry.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly JsonDataStore _store;
    private readonly DataPaths _paths;

    public CharactersGatewayService(JsonDataStore store)
    {
        _store = store;
        _paths = store.Paths;
        if (!Client.DefaultRequestHeaders.UserAgent.Any()) Client.DefaultRequestHeaders.UserAgent.ParseAdd("SoulExe/1.0 (+https://github.com/jofizcd/Soul-of-Waifu)");
        if (!Client.DefaultRequestHeaders.AcceptLanguage.Any()) Client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en-US;q=0.8,en;q=0.6");
    }

    public async Task<IReadOnlyList<GatewayAssetItem>> GetAssetsAsync(string category, string query, bool includeNsfw, int page = 1, int first = 30, CancellationToken token = default)
    {
        var normalized = (category ?? "soul").Trim().ToLowerInvariant();
        return normalized switch
        {
            "soul" => await GetSoulGatewayAsync(query, token),
            "chub" => await GetChubCharactersAsync(query, includeNsfw, page, first, token),
            "lorebooks" => await GetLorebooksAsync(query, token),
            "scenarios" => await GetScenariosAsync(query, token),
            _ => []
        };
    }

    public async Task<GatewayCharacterDetails> GetDetailsAsync(string fullPath, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) throw new ArgumentException("Не указан путь карточки Characters Gateway.", nameof(fullPath));
        var encodedPath = string.Join('/', fullPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        using var response = await Client.GetAsync($"https://gateway.chub.ai/api/characters/{encodedPath}?full=true", token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var node = GetObject(document.RootElement, "node") ?? GetObject(GetObject(document.RootElement, "data"), "node") ?? throw new InvalidDataException("Characters Gateway вернул карточку в неизвестном формате.");
        var definition = GetObject(node, "definition") ?? node;
        var alternate = GetArray(definition, "alternate_greetings").Select(GetString).Where(x => x.Length > 0).ToList();
        return new GatewayCharacterDetails(fullPath, GetString(node, "name", "Unknown"), GetString(node, "tagline"), GetString(node, "avatar_url"), GetString(definition, "description"), GetString(definition, "personality", GetString(definition, "tavern_personality")), GetString(definition, "scenario"), GetString(definition, "first_message"), GetString(definition, "example_dialogs"), alternate);
    }

    public async Task<SoulCharacter> ImportChubCharacterAsync(GatewayCharacterDetails details, CancellationToken token = default)
    {
        var character = new SoulCharacter
        {
            Name = details.Name,
            Title = details.Tagline,
            Description = details.Description,
            Personality = details.Personality,
            Scenario = details.Scenario,
            ExampleDialogue = details.ExampleDialogues,
            SystemPrompt = "Оставайся в образе персонажа. Учитывай карточку, лор, память и историю диалога. Не выходи из роли.",
            SourceType = "characters_gateway",
            SourceUrl = $"https://chub.ai/characters/{details.FullPath}"
        };
        if (Uri.TryCreate(details.AvatarUrl, UriKind.Absolute, out var avatarUri) && avatarUri.Scheme == Uri.UriSchemeHttps) character.AvatarPath = await DownloadAvatarAsync(avatarUri, character.Id, token);
        return await _store.MutateAsync(root =>
        {
            character.Name = MakeUniqueCharacterName(root, character.Name);
            root.Characters.Add(character);
            return character;
        }, "import_chub_gateway", token);
    }

    public async Task<string> DownloadOfficialCharacterCardAsync(GatewayAssetItem item, CancellationToken token = default)
    {
        if (item.Kind != "soul" || string.IsNullOrWhiteSpace(item.DownloadUrl)) throw new InvalidOperationException("Для этой карточки отсутствует файл Character Card V2.");
        var directory = Path.Combine(_paths.Root, "gateway_imports");
        Directory.CreateDirectory(directory);
        var safeName = string.Concat(item.Name.Select(x => char.IsLetterOrDigit(x) || x is '-' or '_' ? x : '_'));
        var path = Path.Combine(directory, $"{safeName}_{Guid.NewGuid():N}.png");
        using var response = await Client.GetAsync(item.DownloadUrl, token);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(path);
        await response.Content.CopyToAsync(output, token);
        return path;
    }

    public async Task<SoulLorebook> ImportLorebookAsync(GatewayAssetItem item, CancellationToken token = default)
    {
        if (item.Kind != "lorebook" || string.IsNullOrWhiteSpace(item.DownloadUrl)) throw new InvalidOperationException("Для этого лорбука отсутствует файл импорта.");
        using var document = await GetDocumentAsync(item.DownloadUrl, token);
        var root = document.RootElement;
        var now = DateTimeOffset.Now;
        var lorebook = new SoulLorebook
        {
            Name = GetString(root, "name", item.Name),
            Description = string.IsNullOrWhiteSpace(item.Description) ? $"Импортировано из Characters Gateway. Автор: {item.Author}" : item.Description,
            SourceId = item.Id,
            CreatedAt = now,
            UpdatedAt = now
        };
        var defaultDepth = GetInt(root, "n_depth");
        var number = 0;
        foreach (var entry in GetArray(root, "entries"))
        {
            number++;
            var probability = GetDouble(entry, "probability", 100);
            if (probability > 1) probability /= 100d;
            lorebook.Entries.Add(new SoulLoreEntry
            {
                Name = GetString(entry, "name", $"Запись {number}"),
                Content = GetString(entry, "content"),
                Keywords = GetArray(entry, "key").Select(GetString).Where(x => x.Length > 0).ToList(),
                SecondaryKeywords = GetArray(entry, "exclude_key").Select(GetString).Where(x => x.Length > 0).ToList(),
                TriggerMode = GetString(entry, "trigger_type", "keyword"),
                InjectionMode = GetString(entry, "injection_behavior", "passive"),
                Depth = GetInt(entry, "depth", defaultDepth),
                TokenBudget = Math.Max(64, GetInt(entry, "token_budget", 512)),
                Probability = Math.Clamp(probability, 0, 1),
                Conditions = new Dictionary<string, string>
                {
                    ["semantic_trigger"] = GetString(entry, "semantic_trigger"),
                    ["cooldown"] = GetInt(entry, "cooldown").ToString(),
                    ["delay"] = GetInt(entry, "delay").ToString(),
                    ["min_msg"] = GetInt(entry, "min_msg").ToString(),
                    ["max_msg"] = GetInt(entry, "max_msg", 9999).ToString()
                }
            });
        }
        return await _store.MutateAsync(data =>
        {
            lorebook.Name = MakeUniqueLorebookName(data, lorebook.Name);
            data.Lorebooks.Add(lorebook);
            return lorebook;
        }, "import_gateway_lorebook", token);
    }

    public async Task<SoulCharacter> ImportTextScenarioAsync(GatewayAssetItem item, CancellationToken token = default)
    {
        if (item.Kind != "scenario" || string.IsNullOrWhiteSpace(item.DownloadUrl)) throw new InvalidOperationException("Для этого сценария отсутствует файл импорта.");
        using var document = await GetDocumentAsync(item.DownloadUrl, token);
        var data = document.RootElement;
        var title = GetString(data, "title", item.Name);
        var worldContext = GetString(data, "world_context");
        var narratorStyle = GetString(data, "narrator_style");
        var location = GetString(data, "starting_location", item.Location);
        var time = GetString(data, "time_of_day");
        var character = new SoulCharacter
        {
            Name = $"{title} — Ведущий",
            Title = "Импортированный текстовый сценарий",
            Description = GetString(data, "description", item.Description),
            Personality = $"Ты — ведущий текстовой ролевой истории. Стиль: {narratorStyle}",
            Scenario = $"Мир и завязка:\n{worldContext}\n\nСтартовая локация: {location}\nВремя: {time}",
            SystemPrompt = $"Веди текстовый сценарий «{title}». Не упоминай Soul Stage, 3D или голосовые модули. Подавай сцену как обычный локальный текстовый чат. {narratorStyle}",
            SourceType = "gateway_text_scenario",
            SourceUrl = item.DownloadUrl
        };
        return await _store.MutateAsync(root =>
        {
            character.Name = MakeUniqueCharacterName(root, character.Name);
            root.Characters.Add(character);
            return character;
        }, "import_gateway_text_scenario", token);
    }

    private async Task<IReadOnlyList<GatewayAssetItem>> GetSoulGatewayAsync(string query, CancellationToken token)
    {
        using var document = await GetDocumentAsync(SoulRegistryUrl, token);
        var items = GetArray(document.RootElement, "characters").Select(x => new GatewayAssetItem("soul", GetString(x, "download_url"), GetString(x, "name", "Без названия"), GetString(x, "author", "Soul Gateway"), "Официальная Character Card V2 из Soul Gateway. Включает карточку персонажа и доступный в ней лор.", GetString(x, "download_url"), GetString(x, "download_url"), 0, 0, 0, 0, "")).ToList();
        return Filter(items, query);
    }

    private async Task<IReadOnlyList<GatewayAssetItem>> GetLorebooksAsync(string query, CancellationToken token)
    {
        using var document = await GetDocumentAsync(LorebooksRegistryUrl, token);
        var items = GetArray(document.RootElement, "lorebooks").Select(x => new GatewayAssetItem("lorebook", GetString(x, "download_url"), GetString(x, "name", "Без названия"), GetString(x, "author", "Unknown"), GetString(x, "description"), "", GetString(x, "download_url"), 0, 0, 0, GetInt(x, "entry_count"), "")).ToList();
        return Filter(items, query);
    }

    private async Task<IReadOnlyList<GatewayAssetItem>> GetScenariosAsync(string query, CancellationToken token)
    {
        using var document = await GetDocumentAsync(StagesRegistryUrl, token);
        var items = GetArray(document.RootElement, "scenes").Select(x => new GatewayAssetItem("scenario", GetString(x, "id", GetString(x, "download_url")), GetString(x, "title", "Без названия"), GetString(x, "author", "Unknown"), GetString(x, "description"), "", GetString(x, "download_url"), 0, 0, 0, 0, GetString(x, "starting_location"))).ToList();
        return Filter(items, query);
    }

    private async Task<IReadOnlyList<GatewayAssetItem>> GetChubCharactersAsync(string query, bool includeNsfw, int page, int first, CancellationToken token)
    {
        var nsfw = includeNsfw ? "true" : "false";
        var size = Math.Clamp(first, 1, 100);
        var number = Math.Max(1, page);
        string parameters;
        if (string.IsNullOrWhiteSpace(query))
        {
            parameters = $"first={size}&page={number}&namespace=characters&nsfw={nsfw}&nsfw_only=false&nsfl=false&min_tokens=100&max_tokens=100000&chub=true&sort=trending&venus=true&count=false";
        }
        else
        {
            var encoded = Uri.EscapeDataString(query.Trim());
            var exclude = includeNsfw ? "" : "excludetopics=NSFW&";
            parameters = $"{exclude}first={size}&page={number}&namespace=characters&search={encoded}&include_forks=true&nsfw={nsfw}&nsfw_only=false&nsfl=false&asc=false&min_ai_rating=0&min_tokens=100&max_tokens=100000&chub=true&exclude_mine=true&sort=default&topics=&inclusive_or=false&recommended_verified=false&venus=true&count=false";
        }
        using var document = await GetDocumentAsync($"https://gateway.chub.ai/search?{parameters}", token);
        var data = GetObject(document.RootElement, "data") ?? document.RootElement;
        var nodes = GetArray(data, "nodes");
        if (nodes.Count == 0) nodes = GetArray(document.RootElement, "nodes");
        return nodes.Select(x => GetObject(x, "node") ?? x)
            .Select(x => new GatewayAssetItem("chub", GetString(x, "fullPath", GetString(x, "full_path")), GetString(x, "name", "Без названия"), GetString(x, "creator", GetString(x, "author", "Chub AI")), GetString(x, "tagline"), GetString(x, "avatar_url"), "", GetInt(x, "starCount"), GetInt(x, "n_favorites"), GetInt(x, "total_tokens"), 0, ""))
            .Where(x => !string.IsNullOrWhiteSpace(x.Id)).ToList();
    }

    private async Task<JsonDocument> GetDocumentAsync(string url, CancellationToken token)
    {
        using var response = await Client.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token);
    }

    private async Task<string> DownloadAvatarAsync(Uri uri, Guid characterId, CancellationToken token)
    {
        using var response = await Client.GetAsync(uri, token);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        var extension = contentType switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", _ => ".jpg" };
        var path = Path.Combine(_paths.AvatarDirectory, $"{characterId}{extension}");
        await using var destination = File.Create(path);
        await response.Content.CopyToAsync(destination, token);
        return path;
    }

    private static IReadOnlyList<GatewayAssetItem> Filter(IEnumerable<GatewayAssetItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return items.ToList();
        var q = query.Trim();
        return items.Where(x => x.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || x.Author.Contains(q, StringComparison.CurrentCultureIgnoreCase) || x.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private static JsonElement? GetObject(JsonElement? element, string property) => element is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var result) && result.ValueKind == JsonValueKind.Object ? result : null;
    private static List<JsonElement> GetArray(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];
    private static string GetString(JsonElement element) => element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
    private static string GetString(JsonElement element, string property, string fallback = "") => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? GetString(value) : fallback;
    private static int GetInt(JsonElement element, string property, int fallback = 0) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : fallback;
    private static double GetDouble(JsonElement element, string property, double fallback = 0) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetDouble(out var result) ? result : fallback;

    private static string MakeUniqueCharacterName(SoulDataRoot root, string candidate)
    {
        var original = string.IsNullOrWhiteSpace(candidate) ? "Импортированный персонаж" : candidate.Trim();
        var unique = original;
        var suffix = 2;
        while (root.Characters.Any(x => string.Equals(x.Name, unique, StringComparison.CurrentCultureIgnoreCase))) unique = $"{original} {suffix++}";
        return unique;
    }

    private static string MakeUniqueLorebookName(SoulDataRoot root, string candidate)
    {
        var original = string.IsNullOrWhiteSpace(candidate) ? "Импортированный лорбук" : candidate.Trim();
        var unique = original;
        var suffix = 2;
        while (root.Lorebooks.Any(x => string.Equals(x.Name, unique, StringComparison.CurrentCultureIgnoreCase))) unique = $"{original} {suffix++}";
        return unique;
    }
}
