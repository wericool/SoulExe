using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class CharacterCardExportService
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public async Task ExportAsync(SoulCharacter character, string outputPath, CancellationToken token = default)
    {
        if (Path.GetExtension(outputPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(character.AvatarPath) || !File.Exists(character.AvatarPath) || !Path.GetExtension(character.AvatarPath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для экспорта PNG выберите у персонажа существующий PNG-аватар. В остальных случаях экспортируйте карточку в JSON.");
            await ExportPngAsync(character, outputPath, token);
        }
        else
        {
            if (!Path.GetExtension(outputPath).Equals(".json", StringComparison.OrdinalIgnoreCase)) outputPath += ".json";
            var json = JsonSerializer.Serialize(BuildV2Card(character), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, token);
        }
    }

    private static async Task ExportPngAsync(SoulCharacter character, string outputPath, CancellationToken token)
    {
        var source = await File.ReadAllBytesAsync(character.AvatarPath, token);
        if (source.Length < PngSignature.Length || !source.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new InvalidDataException("Аватар не является корректным PNG.");
        var json = JsonSerializer.Serialize(BuildV2Card(character));
        var payload = Encoding.Latin1.GetBytes("chara\0" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        await using var destination = File.Create(outputPath);
        await destination.WriteAsync(PngSignature, token);
        var position = PngSignature.Length;
        var header = new byte[8];
        while (position + 12 <= source.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(source.AsSpan(position, 4));
            if (length < 0 || position + 12L + length > source.Length) throw new InvalidDataException("PNG содержит некорректный чанк.");
            var type = Encoding.ASCII.GetString(source, position + 4, 4);
            if (type == "IEND") await WriteChunkAsync(destination, "tEXt", payload, token);
            await destination.WriteAsync(source.AsMemory(position, 12 + length), token);
            position += 12 + length;
            if (type == "IEND") return;
        }
        throw new InvalidDataException("В PNG не найден завершающий чанк IEND.");
    }

    private static object BuildV2Card(SoulCharacter character) => new
    {
        spec = "chara_card_v2",
        spec_version = "2.0",
        data = new
        {
            name = character.Name,
            description = character.Description,
            personality = character.Personality,
            scenario = character.Scenario,
            first_mes = character.Greetings.OrderBy(x => x.Position).FirstOrDefault(x => x.IsPrimary)?.Text ?? character.Greetings.OrderBy(x => x.Position).FirstOrDefault()?.Text ?? "",
            mes_example = character.ExampleDialogue,
            creatorcomment = character.CreatorNotes,
            alternate_greetings = character.Greetings.OrderBy(x => x.Position).Where(x => !x.IsPrimary).Select(x => x.Text).ToArray(),
            tags = Array.Empty<string>(),
            creator = "SoulExe",
            character_version = "1.0",
            extensions = new { soulexe_source_type = character.SourceType }
        }
    };

    private static async Task WriteChunkAsync(Stream destination, string type, byte[] payload, CancellationToken token)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        await destination.WriteAsync(length, token);
        await destination.WriteAsync(typeBytes, token);
        await destination.WriteAsync(payload, token);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, payload));
        await destination.WriteAsync(crc, token);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xffffffff;
        foreach (var item in type.Concat(data))
        {
            crc ^= item;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
        }
        return crc ^ 0xffffffff;
    }
}
