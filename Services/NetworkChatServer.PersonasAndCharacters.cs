using System.Linq;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Registration boundary for personas, character cards, and their local media.</summary>
public sealed partial class NetworkChatServer
{
    private static string? AvatarUrl(SoulCharacter character) => !string.IsNullOrWhiteSpace(character.AvatarPath) && File.Exists(character.AvatarPath) ? $"/api/characters/{character.Id}/avatar?v={File.GetLastWriteTimeUtc(character.AvatarPath).Ticks}" : null;
    private static string? AvatarUrl(SoulPersona persona) => !string.IsNullOrWhiteSpace(persona.AvatarPath) && File.Exists(persona.AvatarPath) ? $"/api/personas/{persona.Id}/avatar?v={File.GetLastWriteTimeUtc(persona.AvatarPath).Ticks}" : null;
    private static string AvatarContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
    private string? AvatarUrlForCharacter(Guid characterId) => _characters().FirstOrDefault(value => value.Id == characterId) is { } character ? AvatarUrl(character) : null;
    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        return bytes.Length >= 12 && System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP" ? ".webp" : null;
    }
    private object? CharacterMini(Guid id)
    {
        var character = _characters().FirstOrDefault(value => value.Id == id);
        return character is null ? null : new { id = character.Id, name = character.Name, title = character.Title, avatarUrl = AvatarUrl(character) };
    }

    private object CharacterDto(SoulCharacter character) => new
    {
        id = character.Id,
        name = character.Name,
        title = character.Title,
        description = character.Description,
        personality = character.Personality,
        scenario = character.Scenario,
        systemPrompt = character.SystemPrompt,
        cognitiveArchitectureEnabled = character.CognitiveArchitectureEnabled,
        soulMemoryEnabled = character.SoulMemoryEnabled,
        soulMemoryPreset = character.SoulMemoryPreset,
        soulMemoryIntervalMessages = character.SoulMemoryIntervalMessages,
        autoSummaryEnabled = character.AutoSummaryEnabled,
        selectedPersonaId = character.SelectedPersonaId,
        autoSummaryIntervalMessages = character.AutoSummaryIntervalMessages,
        avatarUrl = AvatarUrl(character)
    };

    private void MapPersonaAndCharacterRoutes(WebApplication app)
    {
        app.MapGet("/api/personas", GetPersonasAsync);
        app.MapPost("/api/personas", CreatePersonaAsync);
        app.MapPost("/api/personas/generate", GeneratePersonaAsync);
        app.MapPut("/api/personas/{personaId:guid}", UpdatePersonaAsync);
        app.MapGet("/api/personas/{personaId:guid}/avatar", PersonaAvatarAsync);
        app.MapPost("/api/personas/{personaId:guid}/avatar", UploadPersonaAvatarAsync);
        app.MapGet("/api/characters", () => _characters().Select(CharacterDto));
        app.MapPost("/api/characters", CreateCharacterAsync);
        app.MapPost("/api/characters/generate", GenerateCharacterAsync);
        app.MapPost("/api/characters/{characterId:guid}/expand", ExpandCharacterFieldAsync);
        app.MapPut("/api/characters/{characterId:guid}", UpdateCharacterAsync);
        app.MapGet("/api/characters/{characterId:guid}/avatar", CharacterAvatar);
        app.MapPost("/api/characters/{characterId:guid}/avatar", UploadCharacterAvatarAsync);
    }

    private async Task<IResult> GetPersonasAsync(CancellationToken token)
    {
        var personas = await AppServices.Personas.GetPersonasAsync(token);
        return Results.Ok(personas.Select(PersonaDto));
    }

    private object PersonaDto(SoulPersona persona) => new { id = persona.Id, name = persona.Name, description = persona.Description, promptText = persona.PromptText, avatarUrl = AvatarUrl(persona) };

    private async Task<IResult> CreatePersonaAsync(MobilePersonaCreateRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Укажите имя персоны." });
        var persona = await AppServices.Personas.CreateAsync(request.Name.Trim(), token);
        persona.Description = request.Description?.Trim() ?? "";
        persona.PromptText = request.PromptText?.Trim() ?? "";
        await AppServices.Personas.UpdateAsync(persona, token);
        await NotifyDataChangedAsync();
        return Results.Ok(PersonaDto(persona));
    }

    private async Task<IResult> GeneratePersonaAsync(MobilePersonaGenerateRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Idea)) return Results.BadRequest(new { error = "Кратко опишите персону для генерации." });
        var persona = await _generatePersona(request.Idea.Trim(), token);
        await NotifyDataChangedAsync();
        return Results.Ok(PersonaDto(persona));
    }

    private async Task<IResult> UpdatePersonaAsync(Guid personaId, MobilePersonaUpdateRequest request, CancellationToken token)
    {
        var persona = (await AppServices.Personas.GetPersonasAsync(token)).FirstOrDefault(value => value.Id == personaId);
        if (persona is null) return Results.NotFound(new { error = "Персона не найдена." });
        if (!string.IsNullOrWhiteSpace(request.Name)) persona.Name = request.Name.Trim();
        persona.Description = request.Description?.Trim() ?? "";
        persona.PromptText = request.PromptText?.Trim() ?? "";
        await AppServices.Personas.UpdateAsync(persona, token);
        await NotifyDataChangedAsync();
        return Results.Ok(PersonaDto(persona));
    }
    private async Task<IResult> CreateCharacterAsync(MobileCharacterCreateRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Укажите имя персонажа." });
        var character = await AppServices.CharacterLibrary.CreateCharacterAsync(request.Name.Trim(), token);
        character.Title = request.Title?.Trim() ?? "";
        character.Description = request.Description?.Trim() ?? "";
        character.Personality = request.Personality?.Trim() ?? "";
        character.Scenario = request.Scenario?.Trim() ?? "";
        character.SystemPrompt = request.SystemPrompt?.Trim() ?? "";
        await AppServices.CharacterLibrary.UpdateCharacterAsync(character, token);
        await NotifyDataChangedAsync();
        return Results.Ok(CharacterDto(character));
    }

    private async Task<IResult> GenerateCharacterAsync(MobileCharacterGenerateRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Idea)) return Results.BadRequest(new { error = "Опишите персонажа для генерации." });
        var character = await _generateCharacter(request.Idea.Trim(), token);
        await NotifyDataChangedAsync();
        return Results.Ok(CharacterDto(character));
    }

    private async Task<IResult> ExpandCharacterFieldAsync(Guid characterId, MobileCharacterExpandRequest request, CancellationToken token)
    {
        var field = (request.Field ?? string.Empty).Trim().ToLowerInvariant();
        if (field is not ("description" or "personality" or "scenario")) return Results.BadRequest(new { error = "Можно дополнить только описание, личность или сценарий." });
        var character = await _expandCharacterField(characterId, field, token);
        await NotifyDataChangedAsync();
        return Results.Ok(CharacterDto(character));
    }

    private IResult CharacterAvatar(Guid characterId)
    {
        var character = _characters().FirstOrDefault(value => value.Id == characterId);
        if (character is null || string.IsNullOrWhiteSpace(character.AvatarPath) || !File.Exists(character.AvatarPath)) return Results.NotFound();
        var extension = Path.GetExtension(character.AvatarPath).ToLowerInvariant();
        var contentType = extension switch { ".png" => "image/png", ".webp" => "image/webp", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream" };
        return Results.File(character.AvatarPath, contentType);
    }

    private async Task<IResult> PersonaAvatarAsync(Guid personaId, CancellationToken token)
    {
        var persona = (await AppServices.Personas.GetPersonasAsync(token)).FirstOrDefault(value => value.Id == personaId);
        if (persona is null || string.IsNullOrWhiteSpace(persona.AvatarPath) || !File.Exists(persona.AvatarPath)) return Results.NotFound();
        return Results.File(persona.AvatarPath, AvatarContentType(persona.AvatarPath));
    }

    private async Task<IResult> UploadPersonaAvatarAsync(Guid personaId, HttpRequest request, CancellationToken token)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "Передайте изображение в форме." });
        var persona = (await AppServices.Personas.GetPersonasAsync(token)).FirstOrDefault(value => value.Id == personaId);
        if (persona is null) return Results.NotFound(new { error = "Персона не найдена." });
        var form = await request.ReadFormAsync(token);
        var file = form.Files.GetFile("avatar");
        if (file is null || file.Length <= 0) return Results.BadRequest(new { error = "Выберите изображение аватара." });
        if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { error = "Аватар должен быть не больше 5 МБ." });

        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray();
        var extension = DetectImageExtension(bytes);
        if (extension is null) return Results.BadRequest(new { error = "Поддерживаются изображения PNG, JPEG и WebP." });

        var directory = AppServices.Paths.AvatarDirectory;
        Directory.CreateDirectory(directory);
        foreach (var oldFile in Directory.EnumerateFiles(directory, $"persona_{persona.Id}.*")) File.Delete(oldFile);
        var target = Path.Combine(directory, $"persona_{persona.Id}{extension}");
        await File.WriteAllBytesAsync(target, bytes, token);
        persona.AvatarPath = target;
        await AppServices.Personas.UpdateAsync(persona, token);
        await NotifyDataChangedAsync();
        return Results.Ok(PersonaDto(persona));
    }

    private async Task<IResult> UploadCharacterAvatarAsync(Guid characterId, HttpRequest request, CancellationToken token)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "Передайте изображение в форме." });
        var character = _characters().FirstOrDefault(value => value.Id == characterId);
        if (character is null) return Results.NotFound(new { error = "Персонаж не найден." });
        var form = await request.ReadFormAsync(token);
        var file = form.Files.GetFile("avatar");
        if (file is null || file.Length <= 0) return Results.BadRequest(new { error = "Выберите изображение аватара." });
        if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new { error = "Аватар должен быть не больше 5 МБ." });

        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray();
        var extension = DetectImageExtension(bytes);
        if (extension is null) return Results.BadRequest(new { error = "Поддерживаются изображения PNG, JPEG и WebP." });

        var directory = AppServices.Paths.AvatarDirectory;
        Directory.CreateDirectory(directory);
        foreach (var oldFile in Directory.EnumerateFiles(directory, $"{character.Id}.*")) File.Delete(oldFile);
        var target = Path.Combine(directory, $"{character.Id}{extension}");
        await File.WriteAllBytesAsync(target, bytes, token);
        character.AvatarPath = target;
        await AppServices.CharacterLibrary.UpdateCharacterAsync(character, token);
        await NotifyDataChangedAsync();
        return Results.Ok(CharacterDto(character));
    }

    private async Task<IResult> UpdateCharacterAsync(Guid characterId, MobileCharacterUpdate request, CancellationToken token)
    {
        var character = _characters().FirstOrDefault(value => value.Id == characterId);
        if (character is null) return Results.NotFound(new { error = "Персонаж не найден." });
        character.Name = string.IsNullOrWhiteSpace(request.Name) ? character.Name : request.Name.Trim();
        character.Title = request.Title?.Trim() ?? "";
        character.Description = request.Description?.Trim() ?? "";
        character.Personality = request.Personality?.Trim() ?? "";
        character.Scenario = request.Scenario?.Trim() ?? "";
        character.SystemPrompt = request.SystemPrompt?.Trim() ?? "";
        character.SelectedPersonaId = Guid.TryParse(request.SelectedPersonaId, out var personaId) ? personaId : null;
        if (request.CognitiveArchitectureEnabled is not null) character.CognitiveArchitectureEnabled = request.CognitiveArchitectureEnabled.Value;
        if (request.SoulMemoryEnabled is not null) character.SoulMemoryEnabled = request.SoulMemoryEnabled.Value;
        if (!string.IsNullOrWhiteSpace(request.SoulMemoryPreset)) character.SoulMemoryPreset = request.SoulMemoryPreset;
        if (request.SoulMemoryIntervalMessages is not null) character.SoulMemoryIntervalMessages = request.SoulMemoryIntervalMessages.Value;
        if (request.AutoSummaryEnabled is not null) character.AutoSummaryEnabled = request.AutoSummaryEnabled.Value;
        if (request.AutoSummaryIntervalMessages is not null) character.AutoSummaryIntervalMessages = request.AutoSummaryIntervalMessages.Value;
        await AppServices.CharacterLibrary.UpdateCharacterAsync(character, token);
        await NotifyDataChangedAsync();
        return Results.Ok(CharacterDto(character));
    }


}
