using System.IO;
using System.Text.Json;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

// Совместимость со старыми моделями приложения. Новые данные хранятся через JsonDataStore.
public sealed class LocalStorageService
{
    private readonly string _root;
    private readonly string _dataFile;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public LocalStorageService()
    {
        _root = new DataPaths().Root;
        _dataFile = Path.Combine(_root, "legacy-data.json");
    }

    public async Task<AppData> LoadAsync()
    {
        Directory.CreateDirectory(_root);
        if (!File.Exists(_dataFile))
        {
            var initial = new AppData
            {
                Characters =
                [
                    new CharacterProfile
                    {
                        Name = "Ассистент",
                        Description = "Локальный AI-персонаж",
                        SystemPrompt = "Ты полезный и естественный AI-персонаж. Отвечай по-русски, если пользователь пишет по-русски."
                    }
                ]
            };
            await SaveAsync(initial);
            return initial;
        }

        await using var stream = File.OpenRead(_dataFile);
        return await JsonSerializer.DeserializeAsync<AppData>(stream, _json) ?? new AppData();
    }

    public async Task SaveAsync(AppData data)
    {
        Directory.CreateDirectory(_root);
        var temporary = _dataFile + ".tmp";
        await using (var stream = File.Create(temporary)) await JsonSerializer.SerializeAsync(stream, data, _json);
        File.Move(temporary, _dataFile, overwrite: true);
    }

    public string GetAvatarDirectory()
    {
        var path = Path.Combine(_root, "avatars");
        Directory.CreateDirectory(path);
        return path;
    }
}
