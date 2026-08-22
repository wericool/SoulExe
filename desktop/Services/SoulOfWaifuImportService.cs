using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed record SoulOfWaifuImportReport(int CharactersImported, int ChatsImported, int MessagesImported, int AvatarsCopied, IReadOnlyList<string> Warnings)
{
    public string ToDisplayText() => $"Перенос завершён: персонажей — {CharactersImported}, чатов — {ChatsImported}, сообщений — {MessagesImported}, аватаров — {AvatarsCopied}.";
}

public sealed class SoulOfWaifuImportService
{
    private readonly JsonDataStore _store;
    private readonly DataPaths _paths;
    public SoulOfWaifuImportService(JsonDataStore store) { _store = store; _paths = store.Paths; }

    public async Task<SoulOfWaifuImportReport> ImportAsync(string installationFolder, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(installationFolder) || !Directory.Exists(installationFolder))
            throw new DirectoryNotFoundException("Укажите папку старой установки Soul-of-Waifu.");
        var charactersFile = ResolveFile(installationFolder, "characters.json");
        if (charactersFile is null) throw new FileNotFoundException("В выбранной папке не найден файл characters.json.");
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(charactersFile, token));
        var characterList = GetObject(document.RootElement, "character_list") ?? throw new InvalidDataException("Файл characters.json не содержит character_list.");
        var warnings = new List<string>();
        var candidates = new List<SoulCharacter>();
        var avatarsCopied = 0;
        foreach (var property in characterList.EnumerateObject())
        {
            try
            {
                var character = ConvertCharacter(property.Name, property.Value, installationFolder, warnings, ref avatarsCopied);
                candidates.Add(character);
            }
            catch (Exception ex)
            {
                warnings.Add($"Персонаж «{property.Name}» пропущен: {ex.Message}");
            }
        }
        await _store.CreateBackupAsync("before_sow_import", token);
        var fingerprint = CreateFingerprint(charactersFile);
        var result = await _store.MutateAsync(root =>
        {
            var importedCharacters = 0;
            var chats = 0;
            var messages = 0;
            foreach (var candidate in candidates)
            {
                candidate.Name = MakeUniqueName(root, candidate.Name);
                importedCharacters++;
                chats += candidate.Chats.Count;
                messages += candidate.Chats.Sum(x => x.Messages.Count);
                root.Characters.Add(candidate);
            }
            var report = new SoulOfWaifuImportReport(importedCharacters, chats, messages, avatarsCopied, warnings);
            root.ImportRuns.Add(new SoulImportRun
            {
                SourcePath = installationFolder,
                SourceFingerprint = fingerprint,
                ReportJson = JsonSerializer.Serialize(report)
            });
            return report;
        }, "sow_import", token);
        AppLog.Write($"Soul-of-Waifu import completed from {installationFolder}: {result.ToDisplayText()}");
        return result;
    }

    private SoulCharacter ConvertCharacter(string characterName, JsonElement data, string installationFolder, List<string> warnings, ref int avatarsCopied)
    {
        var now = DateTimeOffset.Now;
        var firstMessage = Text(data, "first_message", Text(data, "first_mes"));
        var character = new SoulCharacter
        {
            Name = characterName,
            Title = Text(data, "title", Text(data, "tagline")),
            Description = Text(data, "description"),
            Personality = Text(data, "personality", Text(data, "tavern_personality")),
            Scenario = Text(data, "scenario"),
            SystemPrompt = Text(data, "system_prompt", "Оставайся в образе персонажа. Не выходи из роли."),
            ExampleDialogue = Text(data, "example_messages", Text(data, "example_dialogue", Text(data, "mes_example"))),
            CreatorNotes = Text(data, "creator_notes"),
            SourceType = "soul_of_waifu_import",
            SourceUrl = installationFolder,
            FolderName = Text(data, "folder_name")
        };
        if (!string.IsNullOrWhiteSpace(firstMessage)) character.Greetings.Add(new SoulGreeting { Text = firstMessage, IsPrimary = true, Position = 0 });
        var alternative = GetArray(data, "alternate_greetings").Select(ValueText).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        for (var i = 0; i < alternative.Count; i++) character.Greetings.Add(new SoulGreeting { Text = alternative[i], Position = i + 1 });
        var avatar = FindAvatar(data, characterName, installationFolder);
        if (avatar is not null)
        {
            character.AvatarPath = CopyAvatar(avatar, character.Id);
            avatarsCopied++;
        }
        var chat = new SoulChat { Name = "Импортированный чат", CreatedAt = now, UpdatedAt = now };
        if (!string.IsNullOrWhiteSpace(firstMessage)) AddMessage(chat, SoulMessageRole.Assistant, character.Name, firstMessage, now);
        var imported = ReadSoulHistory(installationFolder, characterName, warnings);
        foreach (var message in imported)
            AddMessage(chat, message.Role, message.Author, message.Content, now);
        character.Chats.Add(chat);
        character.CurrentChatId = chat.Id;
        return character;
    }

    private List<(SoulMessageRole Role, string Author, string Content)> ReadSoulHistory(string root, string characterName, List<string> warnings)
    {
        var result = new List<(SoulMessageRole, string, string)>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*.soul", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateDirectories(root, "*.soul", SearchOption.AllDirectories).SelectMany(dir => Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)))
                .Take(100).ToArray();
        }
        catch { return result; }
        foreach (var file in files.Where(x => x.Contains(characterName, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var messages = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : GetArrayContainer(doc.RootElement);
                foreach (var item in messages.EnumerateArray())
                {
                    var content = Text(item, "content", Text(item, "message", Text(item, "text")));
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    var sourceRole = Text(item, "role", Text(item, "sender", Text(item, "author"))).ToLowerInvariant();
                    var role = sourceRole is "user" or "human" or "you" ? SoulMessageRole.User : sourceRole is "system" ? SoulMessageRole.System : SoulMessageRole.Assistant;
                    result.Add((role, role == SoulMessageRole.User ? "Пользователь" : characterName, content));
                }
            }
            catch (Exception ex) { warnings.Add($"История «{Path.GetFileName(file)}» не прочитана: {ex.Message}"); }
        }
        return result.Take(1000).ToList();
    }

    private static JsonElement GetArrayContainer(JsonElement root)
    {
        foreach (var key in new[] { "messages", "history", "chat_history", "conversation" })
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array) return array;
        using var empty = JsonDocument.Parse("[]");
        return empty.RootElement.Clone();
    }

    private static void AddMessage(SoulChat chat, SoulMessageRole role, string author, string content, DateTimeOffset timestamp)
    {
        var variant = new SoulMessageVariant { Label = "Импорт", Content = content, CreatedAt = timestamp };
        chat.Messages.Add(new SoulMessage { SequenceNumber = chat.Messages.Count + 1, Role = role, AuthorName = author, CurrentVariantId = variant.Id, Variants = [variant], CreatedAt = timestamp });
    }

    private string? FindAvatar(JsonElement data, string name, string root)
    {
        var configured = Text(data, "avatar", Text(data, "avatar_path", Text(data, "image_path")));
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var direct = Path.IsPathRooted(configured) ? configured : Path.Combine(root, configured);
            if (File.Exists(direct)) return direct;
        }
        try
        {
            var normalized = NormalizeName(name);
            return Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .FirstOrDefault(f => NormalizeName(Path.GetFileNameWithoutExtension(f)) == normalized);
        }
        catch { return null; }
    }

    private string CopyAvatar(string source, Guid id)
    {
        var destination = Path.Combine(_paths.AvatarDirectory, $"{id}{Path.GetExtension(source).ToLowerInvariant()}");
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static string? ResolveFile(string root, string name)
    {
        foreach (var candidate in new[] { Path.Combine(root, name), Path.Combine(root, "app", "configuration", name), Path.Combine(root, "configuration", name) })
            if (File.Exists(candidate)) return candidate;
        return null;
    }
    private static JsonElement? GetObject(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
    private static List<JsonElement> GetArray(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];
    private static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Text(JsonElement root, string name, string fallback = "") => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : fallback;
    private static string NormalizeName(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string MakeUniqueName(SoulDataRoot root, string name) { var baseName = string.IsNullOrWhiteSpace(name) ? "Импортированный персонаж" : name.Trim(); var result = baseName; var n = 2; while (root.Characters.Any(x => string.Equals(x.Name, result, StringComparison.CurrentCultureIgnoreCase))) result = $"{baseName} {n++}"; return result; }
    private static string CreateFingerprint(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
