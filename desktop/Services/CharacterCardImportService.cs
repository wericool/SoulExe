using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

public sealed class CharacterCardImportService
{
    private readonly JsonDataStore _store;
    private readonly DataPaths _paths;
    public CharacterCardImportService(JsonDataStore store)
    {
        _store = store;
        _paths = store.Paths;
    }

    public async Task<SoulCharacter> ImportAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Файл карточки не найден.", path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var json = extension switch
        {
            ".json" => await File.ReadAllTextAsync(path, token),
            ".png" => ReadCharacterJsonFromPng(path),
            _ => throw new InvalidOperationException("Поддерживаются JSON и PNG Character Card V2.")
        };
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.TryGetProperty("data", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : document.RootElement;
        var character = CreateCharacter(data, path);
        return await _store.MutateAsync(root =>
        {
            character.Name = MakeUniqueName(root, character.Name);
            if (extension == ".png") character.AvatarPath = CopyAvatar(path, character.Id);
            root.Characters.Add(character);
            return character;
        }, "import_character_card", token);
    }

    private SoulCharacter CreateCharacter(JsonElement data, string sourcePath)
    {
        var now = DateTimeOffset.Now;
        var character = new SoulCharacter
        {
            Name = Get(data, "name") is { Length: > 0 } name ? name : "Импортированный персонаж",
            Title = Get(data, "creatorcomment", "creator_notes"),
            Description = Get(data, "description"),
            Personality = Get(data, "personality", "tavern_personality"),
            Scenario = Get(data, "scenario"),
            ExampleDialogue = Get(data, "mes_example", "example_dialogue"),
            CreatorNotes = Get(data, "creatorcomment", "creator_notes"),
            SourceType = "character_card_v2",
            SourceUrl = sourcePath,
            SystemPrompt = "Оставайся в образе персонажа. Учитывай описание, личность, сценарий и примеры диалога. Не выходи из роли."
        };
        return character;
    }

    private string CopyAvatar(string source, Guid characterId)
    {
        var destination = Path.Combine(_paths.AvatarDirectory, $"{characterId}{Path.GetExtension(source)}");
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static string ReadCharacterJsonFromPng(string path)
    {
        using var stream = File.OpenRead(path);
        var signature = new byte[8];
        try
        {
            stream.ReadExactly(signature);
        }
        catch (EndOfStreamException)
        {
            throw new InvalidDataException("Файл не является корректным PNG.");
        }
        if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Файл не является корректным PNG.");
        var header = new byte[8];
        var crc = new byte[4];
        while (true)
        {
            try
            {
                stream.ReadExactly(header);
            }
            catch (EndOfStreamException)
            {
                break;
            }
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (length < 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("Некорректный PNG-чанк.");
            var data = new byte[length];
            try
            {
                stream.ReadExactly(data);
                stream.ReadExactly(crc);
            }
            catch (EndOfStreamException)
            {
                break;
            }
            if (type == "tEXt")
            {
                var nullIndex = Array.IndexOf(data, (byte)0);
                if (nullIndex > 0 && Encoding.Latin1.GetString(data, 0, nullIndex) == "chara")
                {
                    var payload = Encoding.Latin1.GetString(data, nullIndex + 1, data.Length - nullIndex - 1);
                    return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                }
            }
            if (type == "IEND") break;
        }
        throw new InvalidDataException("В PNG не найдена Character Card V2-метаинформация 'chara'.");
    }

    private static string Get(JsonElement data, params string[] keys)
    {
        foreach (var key in keys)
            if (data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        return "";
    }

    private static string MakeUniqueName(SoulDataRoot root, string candidate)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Импортированный персонаж" : candidate.Trim();
        var name = baseName;
        var suffix = 2;
        while (root.Characters.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase))) name = $"{baseName} {suffix++}";
        return name;
    }
}
