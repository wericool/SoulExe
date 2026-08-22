using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>Local authenticated mobile web client. It exists only while SoulExe is running.</summary>
public sealed class NetworkChatServer : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly Func<string, string, string, CancellationToken, Task<string>> _ask;
    private readonly Func<IEnumerable<SoulCharacter>> _characters;
    private readonly Func<Guid, string, CancellationToken, Task> _sceneAction;
    private readonly Func<(string Username, string Password)> _credentials;
    private readonly Func<string, CancellationToken, Task<SoulCharacter>> _generateCharacter;
    private readonly Func<Guid, string, CancellationToken, Task<SoulCharacter>> _expandCharacterField;
    private readonly Func<Task> _notifyDataChanged;
    private readonly ConcurrentDictionary<string, byte> _sessions = new(StringComparer.Ordinal);

    public NetworkChatServer(
        Func<string, string, string, CancellationToken, Task<string>> ask,
        Func<IEnumerable<SoulCharacter>> characters,
        Func<Guid, string, CancellationToken, Task> sceneAction,
        Func<(string Username, string Password)> credentials,
        Func<string, CancellationToken, Task<SoulCharacter>> generateCharacter,
        Func<Guid, string, CancellationToken, Task<SoulCharacter>> expandCharacterField,
        Func<Task> notifyDataChanged)
    {
        _ask = ask;
        _characters = characters;
        _sceneAction = sceneAction;
        _credentials = credentials;
        _generateCharacter = generateCharacter;
        _expandCharacterField = expandCharacterField;
        _notifyDataChanged = notifyDataChanged;
    }

    public bool IsRunning => _app is not null;
    public string AccessToken { get; private set; } = "";

    public async Task StartAsync(int port, CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;
        _sessions.Clear();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
                context.Response.Headers.Pragma = "no-cache";
                context.Response.Headers.Expires = "0";
                return Task.CompletedTask;
            });
            var path = context.Request.Path.Value ?? string.Empty;
            if (path == "/" || path == "/api/health" || path == "/api/auth/login") { await next(); return; }
            var session = context.Request.Query["s"].FirstOrDefault() ?? context.Request.Headers["X-SoulExe-Session"].FirstOrDefault() ?? context.Request.Headers["X-SoulText-Session"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(session) || !_sessions.ContainsKey(session))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Выполните вход в мобильный SoulExe." });
                return;
            }
            await next();
        });

        app.MapGet("/", () => Results.Content(MobileStyleWebClient.Content, "text/html; charset=utf-8"));
        app.MapGet("/api/health", () => Results.Ok(new { service = "SoulExe", mobileDiscovery = true }));
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapGet("/api/characters", () => _characters().Select(CharacterDto));
        app.MapPost("/api/characters", CreateCharacterAsync);
        app.MapPost("/api/characters/generate", GenerateCharacterAsync);
        app.MapPost("/api/characters/{characterId:guid}/expand", ExpandCharacterFieldAsync);
        app.MapGet("/api/characters/{characterId:guid}/avatar", CharacterAvatar);
        app.MapPost("/api/characters/{characterId:guid}/avatar", UploadCharacterAvatarAsync);
        app.MapGet("/api/characters/{characterId:guid}/chats", CharacterChats);
        app.MapPost("/api/characters/{characterId:guid}/chats", CreateChatAsync);
        app.MapGet("/api/characters/{characterId:guid}/chats/{chatId:guid}/messages", ChatMessages);
        app.MapPost("/api/chat", SendChatAsync);
        app.MapGet("/api/conversations", GetConversationsAsync);
        app.MapGet("/api/conversations/{conversationId:guid}", GetConversationAsync);
        app.MapPut("/api/characters/{characterId:guid}", UpdateCharacterAsync);
        app.MapGet("/api/scenes", GetScenesAsync);
        app.MapGet("/api/scenes/{sceneId:guid}", GetSceneAsync);
        app.MapPut("/api/scenes/{sceneId:guid}", UpdateSceneAsync);
        app.MapPost("/api/scenes", CreateSceneAsync);
        app.MapPost("/api/scenes/{sceneId:guid}/action", SceneActionAsync);
        app.MapPost("/api/scenes/{sceneId:guid}/director", AddDirectorEventAsync);
        app.MapGet("/scene-client.js", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return Results.Text(SceneClientJavaScript(), "text/javascript; charset=utf-8");
        });

        _app = app;
        await app.StartAsync(cancellationToken);
        AppLog.Write($"Network mobile client started on port {port}; password login enabled for this session.");
    }

    private IResult LoginAsync(MobileLoginRequest request)
    {
        var configured = _credentials();
        var username = request.Username?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;
        if (!FixedEquals(username, configured.Username) || !FixedEquals(password, configured.Password))
            return Results.Unauthorized();
        var session = CreateToken();
        _sessions.TryAdd(session, 0);
        return Results.Ok(new { session });
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
        autoSummaryIntervalMessages = character.AutoSummaryIntervalMessages,
        avatarUrl = AvatarUrl(character)
    };

    private async Task<IResult> GetConversationsAsync(CancellationToken token)
    {
        var conversations = await AppServices.DataStore.ReadAsync(root => new ConversationReadService().ReadAll(root), token);
        return Results.Ok(conversations.Select(ConversationDto));
    }

    private async Task<IResult> GetConversationAsync(Guid conversationId, CancellationToken token)
    {
        var conversation = await AppServices.DataStore.ReadAsync(root => new ConversationReadService().ReadAll(root).FirstOrDefault(value => value.Id == conversationId), token);
        return conversation is null ? Results.NotFound(new { error = "Разговор не найден." }) : Results.Ok(ConversationDto(conversation));
    }

    private static object ConversationDto(ConversationSnapshot conversation) => new
    {
        id = conversation.Id,
        kind = conversation.Kind == ConversationKind.Scene ? "scene" : "direct",
        source = conversation.Source.ToString(),
        name = conversation.Name,
        isPinned = conversation.IsPinned,
        isArchived = conversation.IsArchived,
        summaryText = conversation.SummaryText,
        lastSummarizedSequence = conversation.LastSummarizedSequence,
        createdAt = conversation.CreatedAt,
        updatedAt = conversation.UpdatedAt,
        participants = conversation.Participants.Select(participant => new
        {
            id = participant.Id,
            kind = participant.Kind.ToString(),
            displayName = participant.DisplayName,
            characterId = participant.CharacterId,
            canGenerate = participant.CanGenerate,
            sortOrder = participant.SortOrder
        }),
        messages = conversation.Messages.Select(message => new
        {
            id = message.Id,
            sequenceNumber = message.SequenceNumber,
            kind = message.Kind == ConversationMessageKind.DirectorEvent ? "director" : message.Kind == ConversationMessageKind.SystemEvent ? "system" : "message",
            authorParticipantId = message.AuthorParticipantId,
            author = message.AuthorName,
            content = message.Content,
            createdAt = message.CreatedAt,
            editedAt = message.EditedAt,
            variants = message.Variants.Select(variant => new { id = variant.Id, label = variant.Label, content = variant.Content, createdAt = variant.CreatedAt }),
            attachments = message.Attachments.Select(attachment => new { id = attachment.Id, mediaType = attachment.MediaType, originalName = attachment.OriginalName, createdAt = attachment.CreatedAt })
        }),
        context = new
        {
            initialUserProfile = conversation.Context.InitialUserProfile,
            initialRelationshipContext = conversation.Context.InitialRelationshipContext,
            scenario = conversation.Context.Scenario,
            location = conversation.Context.Location,
            timeContext = conversation.Context.TimeContext,
            mood = conversation.Context.Mood,
            goal = conversation.Context.Goal,
            relationshipContext = conversation.Context.RelationshipContext
        },
        turnState = conversation.TurnState is null ? null : new
        {
            status = conversation.TurnState.Status,
            mode = conversation.TurnState.Mode,
            nextParticipantId = conversation.TurnState.NextParticipantId,
            nextTurnAt = conversation.TurnState.NextTurnAt,
            delaySeconds = conversation.TurnState.DelaySeconds,
            enforceContract = conversation.TurnState.EnforceContract,
            advanceAndAvoidRepetition = conversation.TurnState.AdvanceAndAvoidRepetition
        }
    };

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

    private async Task<IResult> CharacterChats(Guid characterId, CancellationToken token)
    {
        var character = await AppServices.CharacterLibrary.GetCharacterAsync(characterId, token);
        if (character is null) return Results.NotFound(new { error = "Персонаж не найден." });
        return Results.Ok((character.Chats ?? []).Where(chat => !chat.IsArchived).OrderByDescending(chat => chat.UpdatedAt)
            .Select(chat => new { id = chat.Id, name = chat.Name, updatedAt = chat.UpdatedAt, messageCount = chat.Messages?.Count ?? 0 }));
    }

    private async Task<IResult> CreateChatAsync(Guid characterId, MobileNewChatRequest request, CancellationToken token)
    {
        var character = _characters().FirstOrDefault(value => value.Id == characterId);
        if (character is null) return Results.NotFound(new { error = "Персонаж не найден." });
        var chat = await AppServices.CharacterLibrary.CreateChatAsync(characterId, string.IsNullOrWhiteSpace(request.Name) ? "Новый чат" : request.Name, token: token);
        await NotifyDataChangedAsync();
        return Results.Ok(new { id = chat.Id, name = chat.Name, updatedAt = chat.UpdatedAt });
    }

    private async Task<IResult> ChatMessages(Guid characterId, Guid chatId, int? take, CancellationToken token)
    {
        var character = await AppServices.CharacterLibrary.GetCharacterAsync(characterId, token);
        var chat = character?.Chats.FirstOrDefault(value => value.Id == chatId);
        if (chat is null) return Results.NotFound(new { error = "Чат не найден." });
        var limit = Math.Clamp(take ?? 30, 1, 100);
        return Results.Ok((chat.Messages ?? []).OrderBy(message => message.SequenceNumber).TakeLast(limit).Select(message => new
        {
            role = message.Role == SoulMessageRole.User ? "user" : message.Role == SoulMessageRole.Assistant ? "bot" : "system",
            author = string.IsNullOrWhiteSpace(message.AuthorName) ? (message.Role == SoulMessageRole.User ? "Вы" : character!.Name) : message.AuthorName,
            content = message.Variants.FirstOrDefault(value => value.Id == message.CurrentVariantId)?.Content ?? message.Variants.FirstOrDefault()?.Content ?? "",
            createdAt = message.CreatedAt
        }));
    }

    private async Task<IResult> SendChatAsync(HttpContext context, NetworkChatRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(request.CharacterId, out var characterId) || !Guid.TryParse(request.ChatId, out var chatId)) return Results.BadRequest(new { error = "Выберите персонажа и чат." });
        if (string.IsNullOrWhiteSpace(request.Message)) return Results.BadRequest(new { error = "Введите сообщение." });
        if (string.Equals(context.Request.Query["async"], "1", StringComparison.Ordinal))
        {
            var text = request.Message.Trim();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ask(characterId.ToString(), chatId.ToString(), text, CancellationToken.None);
                    await NotifyDataChangedAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Network chat generation failed: {ex}");
                }
            });
            return Results.Accepted($"/api/characters/{characterId}/chats/{chatId}/messages", new { accepted = true });
        }
        var reply = await _ask(characterId.ToString(), chatId.ToString(), request.Message, token);
        await NotifyDataChangedAsync();
        return Results.Ok(new { reply });
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

    private async Task<IResult> GetScenesAsync(CancellationToken token)
    {
        var scenes = await AppServices.Scenes.GetScenesAsync(token);
        return Results.Ok(scenes.Select(scene => new
        {
            id = scene.Id, name = scene.Name, status = scene.Status, updatedAt = scene.UpdatedAt, nextTurnAt = scene.NextTurnAt,
            characterA = CharacterMini(scene.CharacterAId), characterB = CharacterMini(scene.CharacterBId),
            delaySeconds = scene.DelaySeconds, nextCharacterId = scene.NextCharacterId
        }));
    }

    private async Task<IResult> GetSceneAsync(Guid sceneId, int? take, CancellationToken token)
    {
        var scene = await AppServices.Scenes.GetSceneAsync(sceneId, token);
        if (scene is null) return Results.NotFound(new { error = "Сцена не найдена." });
        return Results.Ok(SceneDto(scene, take));
    }

    private async Task<IResult> UpdateSceneAsync(Guid sceneId, MobileSceneUpdateRequest request, CancellationToken token)
    {
        var scene = await AppServices.Scenes.GetSceneAsync(sceneId, token);
        if (scene is null) return Results.NotFound(new { error = "Сцена не найдена." });
        scene.Name = request.Name?.Trim() ?? scene.Name;
        scene.Scenario = request.Scenario?.Trim() ?? "";
        scene.Location = request.Location?.Trim() ?? "";
        scene.TimeContext = request.TimeContext?.Trim() ?? "";
        scene.Mood = request.Mood?.Trim() ?? "";
        scene.Goal = request.Goal?.Trim() ?? "";
        scene.RelationshipContext = request.RelationshipContext?.Trim() ?? "";
        scene.TurnMode = request.TurnMode ?? scene.TurnMode;
        scene.DelaySeconds = request.DelaySeconds;
        scene.EnforceSceneContract = request.EnforceSceneContract;
        scene.AdvanceSceneAndAvoidRepetition = request.AdvanceSceneAndAvoidRepetition;
        if (!string.IsNullOrWhiteSpace(request.CharacterAId) || !string.IsNullOrWhiteSpace(request.CharacterBId))
        {
            var characterAId = scene.CharacterAId;
            var characterBId = scene.CharacterBId;
            if (!string.IsNullOrWhiteSpace(request.CharacterAId) && !Guid.TryParse(request.CharacterAId, out characterAId))
                return Results.BadRequest(new { error = "Первый участник сцены указан неверно." });
            if (!string.IsNullOrWhiteSpace(request.CharacterBId) && !Guid.TryParse(request.CharacterBId, out characterBId))
                return Results.BadRequest(new { error = "Второй участник сцены указан неверно." });
            if (characterAId == characterBId)
                return Results.BadRequest(new { error = "Выберите двух разных участников сцены." });
            scene.CharacterAId = characterAId;
            scene.CharacterBId = characterBId;
            if (scene.NextCharacterId != characterAId && scene.NextCharacterId != characterBId)
                scene.NextCharacterId = characterAId;
        }
        await AppServices.Scenes.UpdateAsync(scene, token);
        await NotifyDataChangedAsync();
        return Results.Ok(SceneDto(scene));
    }

    private async Task<IResult> CreateSceneAsync(MobileSceneCreateRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(request.CharacterAId, out var first) || !Guid.TryParse(request.CharacterBId, out var second) || first == second)
            return Results.BadRequest(new { error = "Выберите двух разных участников сцены." });
        var scene = await AppServices.Scenes.CreateAsync(first, second, request.Name ?? "", request.Scenario ?? "", request.Location ?? "", request.TimeContext ?? "", request.Mood ?? "", request.Goal ?? "", first, request.TurnMode ?? "alternate", request.DelaySeconds, request.EnforceSceneContract, request.RelationshipContext ?? "", request.AdvanceSceneAndAvoidRepetition, token);
        await NotifyDataChangedAsync();
        return Results.Ok(SceneDto(scene));
    }

    private async Task<IResult> SceneActionAsync(HttpContext context, Guid sceneId, MobileSceneActionRequest request, CancellationToken token)
    {
        var action = request.Action ?? "";
        if (string.Equals(context.Request.Query["async"], "1", StringComparison.Ordinal) && string.Equals(action, "next", StringComparison.OrdinalIgnoreCase))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sceneAction(sceneId, action, CancellationToken.None);
                    await NotifyDataChangedAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Write($"Network scene generation failed: {ex}");
                }
            });
            return Results.Accepted($"/api/scenes/{sceneId}", new { accepted = true });
        }
        await _sceneAction(sceneId, action, token);
        await NotifyDataChangedAsync();
        var scene = await AppServices.Scenes.GetSceneAsync(sceneId, token);
        return Results.Ok(scene is null ? new { } : SceneDto(scene));
    }

    private async Task<IResult> AddDirectorEventAsync(Guid sceneId, MobileDirectorRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return Results.BadRequest(new { error = "Введите режиссёрское событие." });
        await AppServices.Scenes.AddDirectorMessageAsync(sceneId, request.Text.Trim(), token);
        await NotifyDataChangedAsync();
        var scene = await AppServices.Scenes.GetSceneAsync(sceneId, token);
        return Results.Ok(scene is null ? new { } : SceneDto(scene));
    }

    private object SceneDto(SoulScene scene, int? take = null)
    {
        IEnumerable<SoulSceneMessage> messages = (scene.Messages ?? []).OrderBy(message => message.SequenceNumber);
        if (take is > 0) messages = messages.TakeLast(Math.Clamp(take.Value, 1, 100));

        return new
        {
            id = scene.Id, name = scene.Name, status = scene.Status, scenario = scene.Scenario, location = scene.Location, timeContext = scene.TimeContext,
            mood = scene.Mood, goal = scene.Goal, relationshipContext = scene.RelationshipContext, turnMode = scene.TurnMode, delaySeconds = scene.DelaySeconds,
            enforceSceneContract = scene.EnforceSceneContract, advanceSceneAndAvoidRepetition = scene.AdvanceSceneAndAvoidRepetition, nextCharacterId = scene.NextCharacterId, nextTurnAt = scene.NextTurnAt,
            characterA = CharacterMini(scene.CharacterAId), characterB = CharacterMini(scene.CharacterBId),
            messages = messages.Select(message => new { kind = message.Kind.ToString().ToLowerInvariant(), speakerId = message.SpeakerCharacterId, author = message.SpeakerName, content = message.Content, createdAt = message.CreatedAt })
        };
    }

    private object? CharacterMini(Guid id)
    {
        var character = _characters().FirstOrDefault(value => value.Id == id);
        return character is null ? null : new { id = character.Id, name = character.Name, title = character.Title, avatarUrl = AvatarUrl(character) };
    }

    private static string? AvatarUrl(SoulCharacter character) => !string.IsNullOrWhiteSpace(character.AvatarPath) && File.Exists(character.AvatarPath)
        ? $"/api/characters/{character.Id}/avatar?v={File.GetLastWriteTimeUtc(character.AvatarPath).Ticks}"
        : null;

    private async Task NotifyDataChangedAsync()
    {
        try { await _notifyDataChanged(); }
        catch (Exception ex) { AppLog.Write($"Network data refresh callback failed: {ex}"); }
    }

    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP") return ".webp";
        return null;
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
        AccessToken = "";
        _sessions.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private static bool FixedEquals(string candidate, string configured)
    {
        var expected = Encoding.UTF8.GetBytes(configured ?? string.Empty);
        var actual = Encoding.UTF8.GetBytes(candidate ?? string.Empty);
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private const string Html = """
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><meta name="theme-color" content="#0b0d17"><title>SoulExe Mobile</title><style>
:root{color-scheme:dark;--bg:#090b14;--panel:#121625;--card:#1a2032;--line:#303952;--txt:#f4f6ff;--mut:#9aa6c7;--accent:#6d3df5;--accent2:#426bff;--action:#f4b860;--quote:#8eccff;--code:#c084fc;--danger:#ff7190}*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden;background:var(--bg);color:var(--txt);font:14px/1.45 Inter,system-ui,sans-serif}.app{height:100dvh;max-width:900px;margin:auto;display:grid;grid-template-rows:auto auto minmax(0,1fr);background:linear-gradient(180deg,#12172a,#090b14)}header{padding:calc(11px + env(safe-area-inset-top)) 14px 10px;display:flex;gap:9px;align-items:center;border-bottom:1px solid var(--line)}.brand{font-weight:800;font-size:17px}.brand span{display:block;color:var(--mut);font-size:10px;font-weight:500}.secure{margin-left:auto;color:#bdc9ff;font-size:10px}.nav{display:grid;grid-template-columns:repeat(3,1fr);gap:7px;padding:9px 12px;background:#101424;border-bottom:1px solid var(--line)}button,select,input,textarea{font:inherit}button{border:0;border-radius:11px;background:linear-gradient(135deg,var(--accent),var(--accent2));color:#fff;font-weight:750;padding:10px;cursor:pointer}.nav button{padding:8px;background:#1a2032;color:var(--mut);box-shadow:none}.nav button.active{background:var(--accent);color:white}.view{min-height:0;overflow:auto;padding:13px 12px 18px}.card{background:rgba(22,27,45,.93);border:1px solid var(--line);border-radius:14px;padding:12px;margin-bottom:10px}.grid{display:grid;gap:9px}.two{grid-template-columns:1fr 1fr}label{font-size:10px;color:var(--mut);font-weight:750;letter-spacing:.06em}select,input,textarea{width:100%;color:var(--txt);background:#1b2135;border:1px solid #39435f;border-radius:10px;padding:10px;outline:0}textarea{resize:vertical;min-height:70px}.row{display:flex;gap:8px;align-items:center}.row>*{min-width:0}.row .grow{flex:1}.muted{color:var(--mut);font-size:11px}.history{min-height:40vh;max-height:55vh;overflow:auto;padding:4px 1px}.msg{display:flex;margin:9px 0}.msg.user{justify-content:flex-end}.bubble{max-width:85%;border:1px solid var(--line);background:#1f2633;border-radius:15px;border-bottom-left-radius:5px;padding:9px 11px;white-space:pre-wrap;overflow-wrap:anywhere}.msg.user .bubble{background:linear-gradient(135deg,#5278ff,#345df5);border-color:#9badff;border-bottom-left-radius:15px;border-bottom-right-radius:5px}.meta{font-size:10px;color:var(--mut);margin-bottom:3px}.msg.user .meta{text-align:right}.act{color:var(--action);font-style:italic}.quote{color:var(--quote)}code{color:var(--code);background:#241b39;padding:1px 4px;border-radius:4px}.composer{position:sticky;bottom:-18px;padding:9px 0 calc(9px + env(safe-area-inset-bottom));display:flex;gap:8px;background:linear-gradient(180deg,transparent,#090b14 20%)}.composer textarea{min-height:46px;max-height:110px}.icon{flex:0 0 48px}.avatar{width:36px;height:36px;flex:0 0 36px;border-radius:50%;display:grid;place-items:center;font-weight:800;background:linear-gradient(145deg,#687fff,#3048ca);overflow:hidden}.avatar img{width:100%;height:100%;object-fit:cover}.scenehead{display:flex;gap:8px;align-items:center}.pill{display:inline-block;border-radius:20px;padding:3px 8px;background:#272e48;color:#bdc9ff;font-size:10px;font-weight:700}.danger{background:#6c2940}.soft{background:#222941;color:#dce3ff}.empty{text-align:center;color:var(--mut);padding:32px 10px}.hidden{display:none!important}@media(max-width:390px){.two{grid-template-columns:1fr}.secure{display:none}.bubble{max-width:91%}}
</style></head><body><main class="app"><header><div class="brand">SoulExe <span>локальный мобильный клиент</span></div><div class="secure">Защищённая сессия</div></header><nav class="nav"><button data-tab="chats" class="active">Чаты</button><button data-tab="scenes">Сцены</button><button data-tab="characters">Персонажи</button></nav><section class="view" id="view"></section></main><script>
const view=document.querySelector('#view');let session=sessionStorage.getItem('soulexe-mobile-session')||'',tab='chats',chars=[],scenes=[],busy=false,sceneRefreshTimer=0;const api=(p,o={})=>{let read=!o.method||o.method.toUpperCase()==='GET',url=read?p+(p.includes('?')?'&':'?')+'_='+Date.now():p;o.headers={...(o.headers||{}),'X-SoulExe-Session':session};return fetch(url,{...o,cache:'no-store'})};const json=async r=>{let d=await r.json();if(!r.ok){let e=Error(d.error||'Ошибка доступа');e.status=r.status;throw e}return d};const pause=ms=>new Promise(resolve=>setTimeout(resolve,ms));const esc=s=>(s||'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));const rich=s=>esc(s).replace(/\*\*([^*]+)\*\*/g,'<b>$1</b>').replace(/\*([^*]+)\*/g,'<span class="act">*$1*</span>').replace(/`([^`]+)`/g,'<code>$1</code>').replace(/«([^»]+)»/g,'<span class="quote">«$1»</span>');const tm=t=>new Intl.DateTimeFormat('ru-RU',{hour:'2-digit',minute:'2-digit'}).format(new Date(t));const opt=(a,v,l)=>a.map(x=>`<option value="${x[v]}">${esc(x[l])}</option>`).join('');const by=id=>document.querySelector('#'+id);const avatar=c=>`<div class="avatar">${c?.avatarUrl?`<img src="${c.avatarUrl}?s=${encodeURIComponent(session)}">`:esc((c?.name||'?')[0].toUpperCase())}</div>`;function watchScene(id,lastKey,status){clearTimeout(sceneRefreshTimer);if(status!=='running')return;sceneRefreshTimer=setTimeout(async()=>{if(tab!=='scenes'||by('scene')?.value!==id)return;try{let next=await json(await api('/api/scenes/'+id+'?take=30')),last=next.messages?.at(-1),nextKey=last?last.createdAt+'|'+last.content:'';if(nextKey!==lastKey||next.status!==status)await loadScene(next);else watchScene(id,lastKey,status)}catch{watchScene(id,lastKey,status)}},1000)}
async function load(){chars=await json(await api('/api/characters'));scenes=await json(await api('/api/scenes'));render()};function nav(){document.querySelectorAll('.nav button').forEach(b=>b.classList.toggle('active',b.dataset.tab===tab))};document.querySelector('.nav').onclick=e=>{let b=e.target.closest('button');if(b){tab=b.dataset.tab;render()}};
function render(){nav();if(tab==='chats')chatView();else if(tab==='scenes')webSceneView();else characterView()}
function chatView(){if(!chars.length){view.innerHTML='<div class="empty">Создайте персонажа в SoulExe.</div>';return}view.innerHTML=`<div class="card grid two"><div><label>ПЕРСОНАЖ</label><select id="char">${opt(chars,'id','name')}</select></div><div><label>ЧАТ</label><select id="chat"></select></div><button id="newchat" class="soft">＋ Новый чат</button></div><div id="chatbox" class="card"><div class="muted">Выберите персонажа.</div></div>`;by('char').onchange=loadChats;by('chat').onchange=loadMessages;by('newchat').onclick=()=>newChat(by('char').value);loadChats()}
async function loadChats(){let id=by('char').value,chats=await json(await api('/api/characters/'+id+'/chats'));by('chat').innerHTML=opt(chats,'id','name')||'<option>Нет чатов</option>';if(!chats.length){by('chatbox').innerHTML=`<div class="empty">У этого персонажа ещё нет чатов.<br><button id="newchat">＋ Новый чат</button></div>`;return}by('chatbox').innerHTML='<div class="muted">Загрузка истории…</div>';loadMessages()}
async function newChat(id){let name=prompt('Название нового чата','Новый чат');if(name===null)return;await json(await api('/api/characters/'+id+'/chats',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name})}));await loadChats()}
async function loadMessages(){let cid=by('char').value,qid=by('chat').value;if(!qid)return;let m=await json(await api('/api/characters/'+cid+'/chats/'+qid+'/messages?take=30'));by('chatbox').innerHTML=`<div class="history">${m.map(x=>`<div class="msg ${x.role==='user'?'user':''}"><div><div class="meta">${esc(x.author)} · ${tm(x.createdAt)}</div><div class="bubble">${rich(x.content)}</div></div></div>`).join('')||'<div class="empty">Начните разговор.</div>'}</div><form class="composer" id="sendform"><textarea id="text" placeholder="Напишите сообщение…"></textarea><button class="icon">➤</button></form>`;let h=by('chatbox').querySelector('.history');h.scrollTop=h.scrollHeight;by('sendform').onsubmit=async e=>{e.preventDefault();let text=by('text').value.trim();if(!text||busy)return;busy=true;let add=(author,content,own)=>{let row=document.createElement('div');row.className='msg '+(own?'user':'');row.innerHTML=`<div><div class="meta">${esc(author)} · ${tm(new Date())}</div><div class="bubble">${rich(content)}</div></div>`;h.append(row);h.scrollTop=h.scrollHeight};add('Вы',text,true);by('text').value='';let typing=document.createElement('div');typing.className='muted';typing.textContent='Персонаж формирует ответ…';h.append(typing);h.scrollTop=h.scrollHeight;try{await json(await api('/api/chat?async=1',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({characterId:cid,chatId:qid,message:text})}));for(let attempt=0;attempt<180;attempt++){await pause(1000);if(by('char')?.value!==cid||by('chat')?.value!==qid)return;let current=await json(await api('/api/characters/'+cid+'/chats/'+qid+'/messages?take=30'));let sent=current.findIndex(x=>x.role==='user'&&x.content===text);if(sent>=0&&current.slice(sent+1).some(x=>x.role==='bot')){await loadMessages();return}}typing.textContent='Ответ всё ещё формируется…'}catch(er){typing.remove();alert(er.message)}finally{busy=false}}}
function sceneView(){view.innerHTML=`<div class="card"><div class="row"><div class="grow"><label>СЦЕНА</label><select id="scene">${opt(scenes,'id','name')}</select></div><button id="addscene">＋</button></div></div><div id="scenebox" class="card"></div>`;by('addscene').onclick=newScene;by('scene').onchange=loadScene;if(scenes.length)loadScene();else by('scenebox').innerHTML='<div class="empty">Создайте первую сцену.</div>'}
async function loadScene(s){s=s||await json(await api('/api/scenes/'+by('scene').value+'?take=30'));let people=[s.characterA,s.characterB].filter(Boolean),last=s.messages?.at(-1),lastKey=last?last.createdAt+'|'+last.content:'';by('scenebox').innerHTML=`<div class="scenehead">${people.map(avatar).join('')}<div><b>${esc(s.name)}</b><div class="muted">${esc(s.characterA?.name||'?')} · ${esc(s.characterB?.name||'?')} · <span class="pill">${s.status==='running'?'Идёт':'Пауза'}</span></div></div></div><div class="history">${s.messages.map(x=>`<div class="msg ${x.speakerId===s.characterA?.id?'user':''}"><div><div class="meta">${esc(x.author||'Режиссёр')} · ${tm(x.createdAt)}</div><div class="bubble">${rich(x.content)}</div></div></div>`).join('')||'<div class="empty">Сцена пока без реплик.</div>'}</div><div class="row"><button class="grow soft" id="start">${s.status==='running'?'Пауза':'Старт'}</button><button class="grow" id="next">Следующая реплика</button></div><div class="row" style="margin-top:8px"><input id="director" placeholder="Режиссёрское событие"><button id="direct" class="soft">＋</button></div>`;by('start').onclick=()=>sceneAction(s.id,s.status==='running'?'pause':'start',lastKey);by('next').onclick=()=>sceneAction(s.id,'next',lastKey);by('direct').onclick=async()=>{let text=by('director').value.trim();if(text){let updated=await json(await api('/api/scenes/'+s.id+'/director',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text})}));if(by('scene')?.value===s.id)loadScene(updated)}};let h=by('scenebox').querySelector('.history');requestAnimationFrame(()=>{h.scrollTop=h.scrollHeight});mountSceneSettings(s);watchScene(s.id,lastKey,s.status)}
function mountSceneSettings(s){let d=document.createElement('details');d.className='card';d.innerHTML=`<summary><b>Параметры сцены</b></summary><div class="grid" style="margin-top:10px"><input id="xn" value="${esc(s.name)}" placeholder="Название"><textarea id="xs" placeholder="Сценарий">${esc(s.scenario)}</textarea><div class="two grid"><input id="xl" value="${esc(s.location)}" placeholder="Место"><input id="xt" value="${esc(s.timeContext)}" placeholder="Время"></div><div class="two grid"><input id="xm" value="${esc(s.mood)}" placeholder="Настроение"><input id="xg" value="${esc(s.goal)}" placeholder="Цель"></div><textarea id="xr" placeholder="Отношения / общий контекст">${esc(s.relationshipContext)}</textarea><div class="two grid"><select id="xturn"><option value="alternate">По очереди</option><option value="manual">Вручную</option></select><input id="xdelay" type="number" min="0" max="30" value="${s.delaySeconds}"></div><label><input id="xcontract" type="checkbox" ${s.enforceSceneContract?'checked':''}> Соблюдать рамки сцены</label><label><input id="xadvance" type="checkbox" ${s.advanceSceneAndAvoidRepetition?'checked':''}> Развивать тему и избегать повторов</label><button id="savescene">Сохранить параметры</button></div>`;by('scenebox').append(d);by('xturn').value=s.turnMode||'alternate';by('savescene').onclick=async()=>{let body={name:by('xn').value,scenario:by('xs').value,location:by('xl').value,timeContext:by('xt').value,mood:by('xm').value,goal:by('xg').value,relationshipContext:by('xr').value,turnMode:by('xturn').value,delaySeconds:+by('xdelay').value,enforceSceneContract:by('xcontract').checked,advanceSceneAndAvoidRepetition:by('xadvance').checked};try{await json(await api('/api/scenes/'+s.id,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}));await load();tab='scenes';render();by('scene').value=s.id;loadScene()}catch(e){alert(e.message)}}}
async function sceneAction(id,action,lastKey=''){let wait;if(action==='next'){wait=document.createElement('div');wait.className='muted';wait.textContent='Сцена формирует следующую реплику…';by('scenebox').append(wait)}try{if(action!=='next'){let updated=await json(await api('/api/scenes/'+id+'/action',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({action})}));if(by('scene')?.value===id)await loadScene(updated);return}await json(await api('/api/scenes/'+id+'/action?async=1',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({action})}));for(let attempt=0;attempt<180;attempt++){await pause(1000);if(by('scene')?.value!==id)return;let updated=await json(await api('/api/scenes/'+id+'?take=30')),last=updated.messages?.at(-1),key=last?last.createdAt+'|'+last.content:'';if(key&&key!==lastKey){await loadScene(updated);return}}wait.textContent='Реплика всё ещё формируется…'}catch(e){alert(e.message)}finally{wait?.remove()}}
let webScene={id:'',data:null,busy:false,poll:0,epoch:0};const sceneStamp=s=>{let last=s?.messages?.at(-1);return `${s?.status||''}|${s?.nextCharacterId||''}|${s?.messages?.length||0}|${last?.createdAt||''}|${last?.content||''}`};function clearWebScenePoll(){clearTimeout(webScene.poll);webScene.poll=0}function webSceneView(){clearWebScenePoll();if(!scenes.length){view.innerHTML='<div class="empty">Сцен пока нет. Создайте её в SoulExe на ПК.</div>';return}if(!scenes.some(s=>s.id===webScene.id))webScene.id=scenes[0].id;view.innerHTML=`<div class="card"><div class="row"><div class="grow"><label>СЦЕНА</label><select id="scene">${opt(scenes,'id','name')}</select></div><button id="scenerefresh" class="soft">↻</button></div></div><div id="scenebox" class="card"><div class="muted">Загрузка сцены…</div></div>`;by('scene').value=webScene.id;by('scene').onchange=()=>loadWebScene(by('scene').value);by('scenerefresh').onclick=()=>loadWebScene(webScene.id,true);loadWebScene(webScene.id,true)}async function loadWebScene(id,force=false){if(!id)return;let epoch=++webScene.epoch;try{let next=await json(await api('/api/scenes/'+id+'?take=30'));if(epoch!==webScene.epoch||tab!=='scenes')return;let changed=force||webScene.id!==id||sceneStamp(next)!==sceneStamp(webScene.data);webScene.id=id;webScene.data=next;if(changed)renderWebScene();else scheduleWebScenePoll()}catch(e){if(tab==='scenes'&&epoch===webScene.epoch)by('scenebox').innerHTML=`<div class="empty">Не удалось загрузить сцену: ${esc(e.message)}</div>`}}function renderWebScene(){let s=webScene.data;if(!s||tab!=='scenes'||by('scene')?.value!==webScene.id)return;let people=[s.characterA,s.characterB].filter(Boolean),running=s.status==='running',finished=s.status==='finished';by('scenebox').innerHTML=`<div class="scenehead">${people.map(avatar).join('')}<div class="grow"><b>${esc(s.name)}</b><div class="muted">${esc(s.characterA?.name||'?')} · ${esc(s.characterB?.name||'?')} · <span class="pill">${finished?'Завершена':running?'Идёт':'Пауза'}</span></div></div></div><div class="history">${(s.messages||[]).map(x=>`<div class="msg ${x.speakerId===s.characterA?.id?'user':''}"><div><div class="meta">${esc(x.author||'Режиссёр')} · ${tm(x.createdAt)}</div><div class="bubble">${rich(x.content)}</div></div></div>`).join('')||'<div class="empty">Сцена пока без реплик.</div>'}</div><div class="row"><button class="grow soft" id="sceneStart" ${finished||webScene.busy?'disabled':''}>${running?'Пауза':'Старт'}</button><button class="grow" id="sceneNext" ${finished||webScene.busy?'disabled':''}>Следующая реплика</button></div><div class="row" style="margin-top:8px"><input id="director" placeholder="Режиссёрское событие" ${webScene.busy?'disabled':''}><button id="direct" class="soft" ${webScene.busy?'disabled':''}>＋</button></div><div id="sceneStatus" class="muted" style="margin-top:8px">${webScene.busy?'Сцена обрабатывает действие…':running?'Сцена работает независимо от окна SoulExe.':'Сцена на паузе.'}</div>`;let h=by('scenebox').querySelector('.history');h.scrollTop=h.scrollHeight;by('sceneStart').onclick=()=>webSceneAction(running?'pause':'start');by('sceneNext').onclick=()=>webSceneAction('next');by('direct').onclick=webSceneDirector;scheduleWebScenePoll()}function scheduleWebScenePoll(){clearWebScenePoll();let s=webScene.data;if(!s||s.status!=='running'||tab!=='scenes')return;let stamp=sceneStamp(s),id=webScene.id;webScene.poll=setTimeout(async()=>{if(tab!=='scenes'||webScene.id!==id)return;try{let next=await json(await api('/api/scenes/'+id+'?take=30'));if(webScene.id!==id)return;if(sceneStamp(next)!==stamp){webScene.data=next;renderWebScene()}else scheduleWebScenePoll()}catch{scheduleWebScenePoll()}},1200)}async function webSceneAction(action){let s=webScene.data;if(!s||webScene.busy)return;webScene.busy=true;renderWebScene();let before=sceneStamp(s);try{if(action==='next'){await json(await api('/api/scenes/'+s.id+'/action?async=1',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({action})}));for(let i=0;i<180;i++){await pause(1000);if(tab!=='scenes'||webScene.id!==s.id)return;let next=await json(await api('/api/scenes/'+s.id+'?take=30'));if(sceneStamp(next)!==before){webScene.data=next;return}}}else{webScene.data=await json(await api('/api/scenes/'+s.id+'/action',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({action})}))}}catch(e){alert(e.message)}finally{webScene.busy=false;if(tab==='scenes'&&webScene.id===s.id)renderWebScene()}}async function webSceneDirector(){let s=webScene.data,text=by('director')?.value.trim();if(!s||!text||webScene.busy)return;webScene.busy=true;renderWebScene();try{webScene.data=await json(await api('/api/scenes/'+s.id+'/director',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({text})}))}catch(e){alert(e.message)}finally{webScene.busy=false;if(tab==='scenes'&&webScene.id===s.id)renderWebScene()}}
function newScene(){if(chars.length<2){alert('Для сцены нужно минимум два персонажа.');return}view.innerHTML=`<div class="card grid"><b>Новая сцена</b><div class="two grid"><div><label>УЧАСТНИК A</label><select id="a">${opt(chars,'id','name')}</select></div><div><label>УЧАСТНИК B</label><select id="b">${opt(chars,'id','name')}</select></div></div><input id="sn" placeholder="Название сцены" value="Новая сцена"><textarea id="ss" placeholder="Сценарий и начальная ситуация"></textarea><div class="two grid"><input id="sl" placeholder="Место"><input id="st" placeholder="Время"></div><div class="two grid"><input id="smood" placeholder="Настроение"><input id="sg" placeholder="Цель"></div><textarea id="srel" placeholder="Общие отношения / контекст"></textarea><div class="two grid"><select id="sturn"><option value="alternate">По очереди</option><option value="manual">Вручную</option></select><input id="sdelay" type="number" min="0" max="30" value="10"></div><label><input type="checkbox" id="advance" checked> Развивать сцену и избегать повторов</label><label><input type="checkbox" id="contract" checked> Соблюдать рамки сцены</label><button id="createscene">Создать сцену</button><button class="soft" id="cancel">Отмена</button></div>`;by('cancel').onclick=sceneView;by('createscene').onclick=async()=>{let req={characterAId:by('a').value,characterBId:by('b').value,name:by('sn').value,scenario:by('ss').value,location:by('sl').value,timeContext:by('st').value,mood:by('smood').value,goal:by('sg').value,relationshipContext:by('srel').value,turnMode:by('sturn').value,delaySeconds:+by('sdelay').value,enforceSceneContract:by('contract').checked,advanceSceneAndAvoidRepetition:by('advance').checked};try{let s=await json(await api('/api/scenes',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(req)}));scenes=await json(await api('/api/scenes'));tab='scenes';render();by('scene').value=s.id;loadScene()}catch(e){alert(e.message)}}}
function characterView(){view.innerHTML=`<div class="card row"><div class="grow"><label>ПЕРСОНАЖ</label><select id="editChar">${opt(chars,'id','name')}</select></div><button id="newchar">＋</button></div>${chars.length?`<form id="editForm" class="card grid"><div class="row" id="editHead"></div><div><label>ИМЯ</label><input id="en"></div><div><label>ПОДЗАГОЛОВОК</label><input id="et"></div><div><label>ОПИСАНИЕ</label><textarea id="ed"></textarea><button type="button" class="soft" data-expand="description">Дополнить описание</button></div><div><label>ЛИЧНОСТЬ</label><textarea id="ep"></textarea><button type="button" class="soft" data-expand="personality">Дополнить личность</button></div><div><label>СЦЕНАРИЙ</label><textarea id="es"></textarea><button type="button" class="soft" data-expand="scenario">Дополнить сценарий</button></div><div><label>СИСТЕМНЫЙ ПРОМПТ</label><textarea id="ei"></textarea></div><div class="card grid"><b>Память и Summary</b><label><input type="checkbox" id="ca"> Включить Soul Memory</label><label><input type="checkbox" id="sm"> Тематическая память</label><div class="two grid"><select id="preset"><option value="full">Полный дневник</option><option value="balanced">Баланс</option><option value="light">Лёгкий</option></select><input id="mi" type="number" min="1" max="50" placeholder="Интервал памяти"></div><label><input type="checkbox" id="sum"> Авто-Summary</label><input id="si" type="number" min="1" max="100" placeholder="Интервал Summary"></div><button>Сохранить карточку</button></form>`:'<div class="empty">Создайте первого персонажа.</div>'}`;by('newchar').onclick=newCharacter; if(chars.length){by('editChar').onchange=fillCharacter;by('editForm').onsubmit=saveCharacter;view.querySelectorAll('[data-expand]').forEach(b=>b.onclick=()=>expandField(b.dataset.expand));fillCharacter()}}
function fillCharacter(){let c=chars.find(x=>x.id===by('editChar').value);by('editHead').innerHTML=avatar(c)+`<div><b>${esc(c.name)}</b><div class="muted">Редактирование карточки</div></div>`;[['en','name'],['et','title'],['ed','description'],['ep','personality'],['es','scenario'],['ei','systemPrompt']].forEach(([i,k])=>by(i).value=c[k]||'');by('ca').checked=!!c.cognitiveArchitectureEnabled;by('sm').checked=!!c.soulMemoryEnabled;by('preset').value=c.soulMemoryPreset||'full';by('mi').value=c.soulMemoryIntervalMessages||4;by('sum').checked=!!c.autoSummaryEnabled;by('si').value=c.autoSummaryIntervalMessages||5}
async function newCharacter(){let idea=prompt('Опишите персонажа для генерации. Оставьте пустым, чтобы создать вручную.','');try{if(idea){let c=await json(await api('/api/characters/generate',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({idea})}));chars=await json(await api('/api/characters'));render();by('editChar').value=c.id;fillCharacter();return}let name=prompt('Имя нового персонажа','Новый персонаж');if(!name)return;let c=await json(await api('/api/characters',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name})}));chars=await json(await api('/api/characters'));render();by('editChar').value=c.id;fillCharacter()}catch(e){alert(e.message)}}
async function expandField(field){let id=by('editChar').value;try{let c=await json(await api('/api/characters/'+id+'/expand',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({field})}));chars=chars.map(x=>x.id===c.id?c:x);fillCharacter()}catch(e){alert(e.message)}}
async function saveCharacter(e){e.preventDefault();let id=by('editChar').value,body={name:by('en').value,title:by('et').value,description:by('ed').value,personality:by('ep').value,scenario:by('es').value,systemPrompt:by('ei').value,cognitiveArchitectureEnabled:by('ca').checked,soulMemoryEnabled:by('sm').checked,soulMemoryPreset:by('preset').value,soulMemoryIntervalMessages:+by('mi').value,autoSummaryEnabled:by('sum').checked,autoSummaryIntervalMessages:+by('si').value};try{let c=await json(await api('/api/characters/'+id,{method:'PUT',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)}));chars=chars.map(x=>x.id===c.id?c:x);alert('Карточка и память сохранены.')}catch(er){alert(er.message)}}
function loginView(){view.innerHTML=`<div class="card grid" style="margin-top:12vh"><b>Вход в SoulText</b><div class="muted">Введите логин и пароль, заданные в разделе «Мобильный» на компьютере.</div><input id="login" placeholder="Логин" autocomplete="username"><input id="password" type="password" placeholder="Пароль" autocomplete="current-password"><button id="signin">Войти</button></div>`;by('signin').onclick=async()=>{try{let result=await json(await api('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:by('login').value,password:by('password').value})}));session=result.session;sessionStorage.setItem('soultxt-mobile-session',session);await load()}catch(e){alert('Неверный логин или пароль.')}}}async function boot(){if(!session){loginView();return}try{await load()}catch(e){if(e.status===401){session='';sessionStorage.removeItem('soultxt-mobile-session');loginView()}else view.innerHTML='<div class="empty">'+esc(e.message)+'</div>'}}boot();
</script><script src="/scene-client.js?v=scene-editor-visible-2"></script></body></html>
""";

    private static string SceneClientJavaScript() => """
(() => {
  const baseRenderScene = renderWebScene;
  const webSceneEditor = (scene) => {
    const details = document.createElement('details');
    details.className = 'card';
    details.innerHTML = `<summary><b>Параметры сцены</b></summary><div class="grid" style="margin-top:10px"><input id="webSceneName" value="${esc(scene.name)}" placeholder="Название сцены"><textarea id="webSceneScenario" placeholder="Сценарий">${esc(scene.scenario || '')}</textarea><div class="two grid"><input id="webSceneLocation" value="${esc(scene.location || '')}" placeholder="Место"><input id="webSceneTime" value="${esc(scene.timeContext || '')}" placeholder="Время"></div><div class="two grid"><input id="webSceneMood" value="${esc(scene.mood || '')}" placeholder="Настроение"><input id="webSceneGoal" value="${esc(scene.goal || '')}" placeholder="Цель"></div><textarea id="webSceneRelationship" placeholder="Отношения / общий контекст">${esc(scene.relationshipContext || '')}</textarea><div class="two grid"><select id="webSceneTurn"><option value="alternate">По очереди</option><option value="manual">Вручную</option></select><input id="webSceneDelay" type="number" min="0" max="30" value="${Number(scene.delaySeconds || 0)}"></div><label><input id="webSceneContract" type="checkbox" ${scene.enforceSceneContract ? 'checked' : ''}> Соблюдать рамки сцены</label><label><input id="webSceneAdvance" type="checkbox" ${scene.advanceSceneAndAvoidRepetition ? 'checked' : ''}> Развивать тему и избегать повторов</label><button id="webSceneSave" class="soft">Сохранить параметры</button><div id="webSceneSaveStatus" class="muted"></div></div>`;
    const sceneBox = by('scenebox'), history = sceneBox.querySelector('.history');
    sceneBox.insertBefore(details, history);
    const actionRow = by('sceneNext')?.parentElement;
    if (actionRow) {
      const settingsButton = document.createElement('button');
      settingsButton.id = 'webSceneSettings'; settingsButton.className = 'soft'; settingsButton.textContent = '⚙ Параметры сцены';
      settingsButton.onclick = () => { details.open = true; details.scrollIntoView({ behavior: 'smooth', block: 'start' }); };
      actionRow.append(settingsButton);
    }
    details.querySelector('#webSceneTurn').value = scene.turnMode || 'alternate';
    details.querySelector('#webSceneSave').onclick = async () => {
      const status = details.querySelector('#webSceneSaveStatus');
      const body = { name: details.querySelector('#webSceneName').value, scenario: details.querySelector('#webSceneScenario').value, location: details.querySelector('#webSceneLocation').value, timeContext: details.querySelector('#webSceneTime').value, mood: details.querySelector('#webSceneMood').value, goal: details.querySelector('#webSceneGoal').value, relationshipContext: details.querySelector('#webSceneRelationship').value, turnMode: details.querySelector('#webSceneTurn').value, delaySeconds: +details.querySelector('#webSceneDelay').value, enforceSceneContract: details.querySelector('#webSceneContract').checked, advanceSceneAndAvoidRepetition: details.querySelector('#webSceneAdvance').checked };
      try {
        status.textContent = 'Сохранение…';
        const updated = await json(await api('/api/scenes/' + scene.id, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }));
        scenes = await json(await api('/api/scenes'));
        webScene.id = updated.id; webScene.data = updated;
        status.textContent = 'Параметры применены.';
        renderWebScene();
      } catch (error) { status.textContent = error.message || 'Не удалось сохранить параметры.'; }
    };
  };
  renderWebScene = function () { baseRenderScene(); if (tab === 'scenes' && webScene.data) webSceneEditor(webScene.data); };
  webSceneView = function () {
    clearWebScenePoll();
    if (!scenes.length) { view.innerHTML = '<div class="empty">Сцен пока нет.<br><button id="webSceneCreate">＋ Создать сцену</button></div>'; by('webSceneCreate').onclick = webSceneCreateForm; return; }
    if (!scenes.some(item => item.id === webScene.id)) webScene.id = scenes[0].id;
    view.innerHTML = `<div class="card"><div class="row"><div class="grow"><label>СЦЕНА</label><select id="scene">${opt(scenes,'id','name')}</select></div><button id="webSceneCreate" class="soft">＋</button><button id="scenerefresh" class="soft">↻</button></div></div><div id="scenebox" class="card"><div class="muted">Загрузка сцены…</div></div>`;
    by('scene').value = webScene.id;
    by('scene').onchange = () => loadWebScene(by('scene').value, true);
    by('scenerefresh').onclick = () => loadWebScene(webScene.id, true);
    by('webSceneCreate').onclick = webSceneCreateForm;
    loadWebScene(webScene.id, true);
  };
  function webSceneCreateForm() {
    clearWebScenePoll();
    if (chars.length < 2) { view.innerHTML = '<div class="empty">Для сцены нужны два персонажа.</div>'; return; }
    view.innerHTML = `<div class="card grid"><b>Новая сцена</b><div class="two grid"><div><label>УЧАСТНИК A</label><select id="sceneA">${opt(chars,'id','name')}</select></div><div><label>УЧАСТНИК B</label><select id="sceneB">${opt(chars,'id','name')}</select></div></div><input id="sceneName" placeholder="Название" value="Новая сцена"><textarea id="sceneScenario" placeholder="Сценарий и начальная ситуация"></textarea><div class="two grid"><input id="sceneLocation" placeholder="Место"><input id="sceneTime" placeholder="Время"></div><div class="two grid"><input id="sceneMood" placeholder="Настроение"><input id="sceneGoal" placeholder="Цель"></div><textarea id="sceneRelationship" placeholder="Отношения / общий контекст"></textarea><div class="two grid"><select id="sceneTurn"><option value="alternate">По очереди</option><option value="manual">Вручную</option></select><input id="sceneDelay" type="number" min="0" max="30" value="10"></div><label><input id="sceneContract" type="checkbox" checked> Соблюдать рамки сцены</label><label><input id="sceneAdvance" type="checkbox" checked> Развивать тему и избегать повторов</label><button id="sceneCreateSubmit">Создать сцену</button><button id="sceneCreateCancel" class="soft">Отмена</button><div id="sceneCreateStatus" class="muted"></div></div>`;
    const selectA = by('sceneA'), selectB = by('sceneB'); if (chars[1]) selectB.value = chars[1].id;
    by('sceneCreateCancel').onclick = () => { tab = 'scenes'; render(); };
    by('sceneCreateSubmit').onclick = async () => {
      const status = by('sceneCreateStatus');
      if (selectA.value === selectB.value) { status.textContent = 'Выберите двух разных участников.'; return; }
      const body = { characterAId: selectA.value, characterBId: selectB.value, name: by('sceneName').value, scenario: by('sceneScenario').value, location: by('sceneLocation').value, timeContext: by('sceneTime').value, mood: by('sceneMood').value, goal: by('sceneGoal').value, relationshipContext: by('sceneRelationship').value, turnMode: by('sceneTurn').value, delaySeconds: +by('sceneDelay').value, enforceSceneContract: by('sceneContract').checked, advanceSceneAndAvoidRepetition: by('sceneAdvance').checked };
      try { status.textContent = 'Создание…'; const created = await json(await api('/api/scenes', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })); scenes = await json(await api('/api/scenes')); webScene.id = created.id; webScene.data = created; tab = 'scenes'; render(); } catch (error) { status.textContent = error.message || 'Не удалось создать сцену.'; }
    };
  }
})();
""";
}

public sealed record MobileLoginRequest(string? Username, string? Password);
public sealed record MobileNewChatRequest(string? Name);
public sealed record MobileCharacterGenerateRequest(string? Idea);
public sealed record MobileCharacterExpandRequest(string? Field);
public sealed record MobileCharacterCreateRequest(string? Name, string? Title, string? Description, string? Personality, string? Scenario, string? SystemPrompt);
public sealed record MobileCharacterUpdate(string? Name, string? Title, string? Description, string? Personality, string? Scenario, string? SystemPrompt, bool? CognitiveArchitectureEnabled = null, bool? SoulMemoryEnabled = null, string? SoulMemoryPreset = null, int? SoulMemoryIntervalMessages = null, bool? AutoSummaryEnabled = null, int? AutoSummaryIntervalMessages = null);
public sealed record MobileSceneUpdateRequest(string? Name, string? Scenario, string? Location, string? TimeContext, string? Mood, string? Goal, string? RelationshipContext, string? TurnMode, int DelaySeconds, bool EnforceSceneContract, bool AdvanceSceneAndAvoidRepetition, string? CharacterAId = null, string? CharacterBId = null);
public sealed record MobileSceneCreateRequest(string? CharacterAId, string? CharacterBId, string? Name, string? Scenario, string? Location, string? TimeContext, string? Mood, string? Goal, string? RelationshipContext, string? TurnMode, int DelaySeconds, bool EnforceSceneContract, bool AdvanceSceneAndAvoidRepetition);
public sealed record MobileSceneActionRequest(string? Action);
public sealed record MobileDirectorRequest(string? Text);
