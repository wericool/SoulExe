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
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Local authenticated mobile web client. It exists only while SoulExe is running.</summary>
public sealed partial class NetworkChatServer : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly Func<NetworkChatRequest, CancellationToken, Task<string>> _ask;
    private readonly Func<NetworkChatRequest, Action<string>, CancellationToken, Task<string>> _askWithPreview;
    private readonly Func<IEnumerable<SoulCharacter>> _characters;
    private readonly Func<Guid, string, CancellationToken, Task> _sceneAction;
    private readonly Func<(string Username, string PasswordHash)> _credentials;
    private readonly Func<string, CancellationToken, Task<SoulCharacter>> _generateCharacter;
    private readonly Func<string, CancellationToken, Task<SoulPersona>> _generatePersona;
    private readonly Func<string, CancellationToken, Task<string>> _expandPersonaDescription;
    private readonly Func<Guid, string, CancellationToken, Task<SoulCharacter>> _expandCharacterField;
    private readonly Func<Task> _notifyDataChanged;
    private readonly NetworkSessionStore _sessions = new();
    private readonly ConcurrentDictionary<Guid, NetworkGenerationPreview> _generationPreviews = new();

    public NetworkChatServer(
        Func<NetworkChatRequest, CancellationToken, Task<string>> ask,
        Func<NetworkChatRequest, Action<string>, CancellationToken, Task<string>> askWithPreview,
        Func<IEnumerable<SoulCharacter>> characters,
        Func<Guid, string, CancellationToken, Task> sceneAction,
        Func<(string Username, string PasswordHash)> credentials,
        Func<string, CancellationToken, Task<SoulCharacter>> generateCharacter,
        Func<string, CancellationToken, Task<SoulPersona>> generatePersona,
        Func<string, CancellationToken, Task<string>> expandPersonaDescription,
        Func<Guid, string, CancellationToken, Task<SoulCharacter>> expandCharacterField,
        Func<Task> notifyDataChanged)
    {
        _ask = ask;
        _askWithPreview = askWithPreview;
        _characters = characters;
        _sceneAction = sceneAction;
        _credentials = credentials;
        _generateCharacter = generateCharacter;
        _generatePersona = generatePersona;
        _expandPersonaDescription = expandPersonaDescription;
        _expandCharacterField = expandCharacterField;
        _notifyDataChanged = notifyDataChanged;
    }

    public bool IsRunning => _app is not null;
    public int SessionCount => _sessions.Count;

    public void InvalidateSessions() => _sessions.Clear();

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
            var session = context.Request.Query["s"].FirstOrDefault() ?? context.Request.Headers["X-SoulExe-Session"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(session) || !_sessions.TryAuthorize(session, DateTimeOffset.UtcNow))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { error = "Выполните вход в мобильный SoulExe." });
                return;
            }
            await next();
        });

        MapCoreRoutes(app);
        MapPersonaAndCharacterRoutes(app);
        MapConversationRoutes(app);

        _app = app;
        await app.StartAsync(cancellationToken);
        AppLog.Write($"Network mobile client started on port {port}; password login enabled for this session.");
    }

    private static object ConversationDto(ConversationSnapshot conversation, int? take = null, Func<Guid, string?>? avatarUrl = null)
    {
        var capabilities = ConversationCapabilityPolicy.For(conversation);
        IEnumerable<ConversationMessageSnapshot> messages = conversation.Messages.OrderBy(message => message.SequenceNumber);
        if (take is > 0) messages = messages.TakeLast(take.Value);
        return new
        {
            id = conversation.Id,
            mode = conversation.Mode == ConversationMode.Group ? "group" : "personal",
            source = conversation.Source.ToString(),
            name = conversation.Name,
            isPinned = conversation.IsPinned,
            isArchived = conversation.IsArchived,
            summaryText = conversation.SummaryText,
            lastSummarizedSequence = conversation.LastSummarizedSequence,
            createdAt = conversation.CreatedAt,
            updatedAt = conversation.UpdatedAt,
            capabilities = new
            {
                appendUserMessage = capabilities.CanAppendUserMessage,
                addDirectorEvent = capabilities.CanAddDirectorEvent,
                start = capabilities.CanStart,
                pause = capabilities.CanPause,
                finish = capabilities.CanFinish,
                chooseNextParticipant = capabilities.CanChooseNextParticipant,
                generateNextTurn = capabilities.CanGenerateNextTurn,
                pin = capabilities.CanPin,
                rename = capabilities.CanRename,
                delete = capabilities.CanDelete
            },
            participants = conversation.Participants.Select(participant => new
            {
                id = participant.Id,
                kind = participant.Kind.ToString(),
                displayName = participant.DisplayName,
                characterId = participant.CharacterId,
                avatarUrl = participant.CharacterId is { } characterId ? avatarUrl?.Invoke(characterId) : null,
                canGenerate = participant.CanGenerate,
                sortOrder = participant.SortOrder
            }),
            messages = messages.Select(message => new
            {
                id = message.Id,
                sequenceNumber = message.SequenceNumber,
                kind = message.Kind == ConversationMessageKind.DirectorEvent ? "director" : message.Kind == ConversationMessageKind.SystemEvent ? "system" : "message",
                authorParticipantId = message.AuthorParticipantId,
                authorKind = message.AuthorKind.ToString().ToLowerInvariant(),
                authorPersonaId = message.AuthorPersonaId,
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
    }

    private async Task NotifyDataChangedAsync()
    {
        try { await _notifyDataChanged(); }
        catch (Exception ex) { AppLog.Write($"Network data refresh callback failed: {ex}"); }
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
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

}

public sealed record MobileLoginRequest(string? Username, string? Password);
public sealed record MobilePushRegistrationRequest(string? Token, string? Platform = "android", string? DeviceName = null);
public sealed record MobileCharacterGenerateRequest(string? Idea);
public sealed record MobilePersonaGenerateRequest(string? Idea);
public sealed record MobilePersonaCreateRequest(string? Name, string? Description = null, string? PromptText = null);
public sealed record MobilePersonaUpdateRequest(string? Name, string? Description, string? PromptText);
public sealed record MobileCharacterExpandRequest(string? Field);
public sealed record MobileCharacterCreateRequest(
    string? Name,
    string? Title,
    string? Description,
    string? Personality,
    string? Scenario,
    string? SystemPrompt,
    bool? CognitiveArchitectureEnabled = null,
    bool? SoulMemoryEnabled = null,
    string? SoulMemoryPreset = null,
    int? SoulMemoryIntervalMessages = null,
    bool? AutoSummaryEnabled = null,
    int? AutoSummaryIntervalMessages = null,
    string? SelectedPersonaId = null,
    string? PersonalityExpressionLevel = null,
    string? ReplyLanguage = null,
    bool? UseRoleplayResponseFormatting = null,
    string? DefaultUserProfile = null,
    string? DefaultRelationshipContext = null,
    string? ExampleDialogue = null,
    string? SelectedPromptPresetId = null,
    string[]? LorebookIds = null,
    bool? ProactiveMessagesEnabled = null,
    bool? ProactiveQuietHoursEnabled = null,
    string? ProactiveQuietHoursStart = null,
    string? ProactiveQuietHoursEnd = null,
    bool? RealisticMessagingEnabled = null);
public sealed record MobileCharacterUpdate(string? Name, string? Title, string? Description, string? Personality, string? Scenario, string? SystemPrompt, bool? CognitiveArchitectureEnabled = null, bool? SoulMemoryEnabled = null, string? SoulMemoryPreset = null, int? SoulMemoryIntervalMessages = null, bool? AutoSummaryEnabled = null, int? AutoSummaryIntervalMessages = null, string? SelectedPersonaId = null, string? PersonalityExpressionLevel = null, string? ReplyLanguage = null, bool? UseRoleplayResponseFormatting = null, string? DefaultUserProfile = null, string? DefaultRelationshipContext = null, string? ExampleDialogue = null, string? SelectedPromptPresetId = null, string[]? LorebookIds = null, bool? ProactiveMessagesEnabled = null, bool? ProactiveQuietHoursEnabled = null, string? ProactiveQuietHoursStart = null, string? ProactiveQuietHoursEnd = null, bool? RealisticMessagingEnabled = null);
public sealed record MobileConversationActionRequest(string? Action, string? Text = null, string? AuthorKind = null, string? AuthorPersonaId = null);
internal sealed record NetworkGenerationPreview(string Text, bool IsGenerating, string? Error = null);
public sealed record MobileConversationCreateRequest(
    string[]? CharacterIds,
    string? Name,
    string? Scenario = null,
    string? Location = null,
    string? TimeContext = null,
    string? Mood = null,
    string? Goal = null,
    string? RelationshipContext = null,
    string? TurnMode = null,
    int DelaySeconds = 0,
    bool EnforceContract = true,
    bool AdvanceAndAvoidRepetition = true);
public sealed record MobileConversationUpdateRequest(
    string[]? CharacterIds = null,
    string? Name = null,
    string? Scenario = null,
    string? Location = null,
    string? TimeContext = null,
    string? Mood = null,
    string? Goal = null,
    string? RelationshipContext = null,
    string? TurnMode = null,
    int? DelaySeconds = null,
    bool? EnforceContract = null,
    bool? AdvanceAndAvoidRepetition = null);
