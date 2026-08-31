using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Registration boundary for the canonical conversation API.</summary>
public sealed partial class NetworkChatServer
{
    private static IReadOnlyList<ConversationSnapshot> ReadConversations(SoulDataRoot root) => root.Conversations ?? [];

    private void MapConversationRoutes(WebApplication app)
    {
        app.MapGet("/api/conversations", GetConversationsAsync);
        app.MapPost("/api/conversations", CreateConversationAsync);
        app.MapPut("/api/conversations/{conversationId:guid}", UpdateConversationAsync);
        app.MapGet("/api/conversations/page", GetConversationPageAsync);
        app.MapGet("/api/conversations/{conversationId:guid}", GetConversationAsync);
        app.MapGet("/api/conversations/{conversationId:guid}/generation-preview", GetGenerationPreview);
        app.MapPost("/api/conversations/{conversationId:guid}/actions", ConversationActionAsync);
    }

    private async Task<IResult> UpdateConversationAsync(Guid conversationId, MobileConversationUpdateRequest request, CancellationToken token)
    {
        ConversationAddress address;
        try { address = await AppServices.Conversations.ResolveAddressAsync(conversationId, token); }
        catch (InvalidOperationException exception) { return Results.NotFound(new { error = exception.Message }); }

        if (address.Kind == ConversationKind.Direct)
        {
            if (!string.IsNullOrWhiteSpace(request.Name))
                await AppServices.Conversations.RenameAsync(address, request.Name, token);
        }
        else
        {
            var ids = (request.CharacterIds ?? []).Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count != 2) return Results.BadRequest(new { error = "Выберите двух разных персонажей." });
            var current = await AppServices.Conversations.GetAsync(address, token);
            var context = current.Conversation.Context;
            var turn = current.Conversation.TurnState;
            await AppServices.Conversations.UpdateGroupAsync(conversationId, ids,
                request.Name ?? current.Conversation.Name, request.Scenario ?? context.Scenario, request.Location ?? context.Location,
                request.TimeContext ?? context.TimeContext, request.Mood ?? context.Mood, request.Goal ?? context.Goal,
                request.RelationshipContext ?? context.RelationshipContext, request.TurnMode ?? turn?.Mode ?? "alternate",
                request.DelaySeconds ?? turn?.DelaySeconds ?? 0, request.EnforceContract ?? turn?.EnforceContract ?? true,
                request.AdvanceAndAvoidRepetition ?? turn?.AdvanceAndAvoidRepetition ?? true, token);
        }

        var result = await AppServices.Conversations.GetAsync(address, token);
        await NotifyDataChangedAsync();
        return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
    }

    private async Task<IResult> CreateConversationAsync(MobileConversationCreateRequest request, CancellationToken token)
    {
        var characterIds = (request.CharacterIds ?? [])
            .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (characterIds.Count is < 1 or > 2)
            return Results.BadRequest(new { error = "Выберите одного или двух разных персонажей." });

        var result = await AppServices.Conversations.CreateAsync(characterIds, request.Name ?? "Новый разговор",
            request.Scenario ?? "", request.Location ?? "", request.TimeContext ?? "", request.Mood ?? "", request.Goal ?? "",
            request.RelationshipContext ?? "", request.TurnMode ?? "alternate", request.DelaySeconds, request.EnforceContract,
            request.AdvanceAndAvoidRepetition, token);
        await NotifyDataChangedAsync();
        return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
    }

    private async Task<IResult> GetConversationsAsync(HttpRequest request, CancellationToken token)
    {
        var take = ConversationPaging.ReadMessageTake(request.Query["take"].ToString());
        var conversations = await AppServices.DataStore.ReadAsync(root =>
        {
            var avatars = (root.Characters ?? []).ToDictionary(character => character.Id, AvatarUrl);
            return ReadConversations(root)
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .ThenBy(conversation => conversation.Id)
                .Select(conversation => ConversationDto(conversation, take, characterId => avatars.GetValueOrDefault(characterId)))
                .ToList();
        }, token);
        return Results.Ok(conversations);
    }

    private async Task<IResult> GetConversationAsync(Guid conversationId, HttpRequest request, CancellationToken token)
    {
        var take = ConversationPaging.ReadMessageTake(request.Query["take"].ToString());
        var conversation = await AppServices.DataStore.ReadAsync(root =>
        {
            var avatars = (root.Characters ?? []).ToDictionary(character => character.Id, AvatarUrl);
            var snapshot = ReadConversations(root).FirstOrDefault(value => value.Id == conversationId);
            return snapshot is null ? null : ConversationDto(snapshot, take, characterId => avatars.GetValueOrDefault(characterId));
        }, token);
        return conversation is null ? Results.NotFound(new { error = "Разговор не найден." }) : Results.Ok(conversation);
    }

    private async Task<IResult> GetConversationPageAsync(HttpRequest request, CancellationToken token)
    {
        var messageTake = ConversationPaging.ReadMessageTake(request.Query["take"].ToString());
        var pageSize = ConversationPaging.ReadPageSize(request.Query["limit"].ToString());
        var cursor = ConversationPaging.ParseCursor(request.Query["cursor"].ToString());
        var page = await AppServices.DataStore.ReadAsync(root =>
        {
            var avatars = (root.Characters ?? []).ToDictionary(character => character.Id, AvatarUrl);
            var eligible = ReadConversations(root).OrderByDescending(conversation => conversation.UpdatedAt).ThenBy(conversation => conversation.Id)
                .Where(conversation => cursor is null || conversation.UpdatedAt < cursor.UpdatedAt || (conversation.UpdatedAt == cursor.UpdatedAt && conversation.Id.CompareTo(cursor.Id) > 0)).ToList();
            var items = eligible.Take(pageSize).ToList();
            var nextCursor = items.Count == pageSize && eligible.Count > items.Count ? ConversationPaging.CreateCursor(items[^1]) : null;
            return new { items = items.Select(conversation => ConversationDto(conversation, messageTake, characterId => avatars.GetValueOrDefault(characterId))).ToList(), nextCursor };
        }, token);
        return Results.Ok(page);
    }

    private IResult GetGenerationPreview(Guid conversationId)
    {
        var preview = _generationPreviews.GetValueOrDefault(conversationId);
        return Results.Ok(preview is null
            ? new { text = "", isGenerating = false, error = (string?)null }
            : new { text = preview.Text, isGenerating = preview.IsGenerating, error = preview.Error });
    }

    private Task StartDirectGenerationWithPreviewAsync(Guid conversationId, NetworkChatRequest request)
    {
        _generationPreviews[conversationId] = new NetworkGenerationPreview("", true);
        return Task.Run(async () =>
        {
        try
        {
            await _askWithPreview(request, text => _generationPreviews[conversationId] = new NetworkGenerationPreview(text, true), CancellationToken.None);
            _generationPreviews[conversationId] = new NetworkGenerationPreview("", false);
            await NotifyDataChangedAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Conversation direct generation failed: {ex}");
            _generationPreviews[conversationId] = new NetworkGenerationPreview("", false, ex.Message);
        }
        });
    }

    private async Task<IResult> ConversationActionAsync(HttpContext context, Guid conversationId, MobileConversationActionRequest request, CancellationToken token)
    {
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        ConversationAddress address;
        try { address = await AppServices.Conversations.ResolveAddressAsync(conversationId, token); }
        catch (InvalidOperationException exception) { return Results.NotFound(new { error = exception.Message }); }
        var kind = address.Kind;

        // The generation still delegates to the established direct-turn runner, but clients no
        // longer need to know that a direct conversation is physically nested in a character.
        if (kind == ConversationKind.Direct && action == "send")
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest(new { error = "Введите сообщение." });

            var target = await AppServices.Conversations.ResolveDirectAsync(conversationId, token);
            var text = request.Text.Trim();
            if (string.Equals(context.Request.Query["async"], "1", StringComparison.Ordinal))
            {
                _ = StartDirectGenerationWithPreviewAsync(conversationId, new NetworkChatRequest(target.CharacterId.ToString(), target.ChatId.ToString(), text, request.AuthorKind, request.AuthorPersonaId));
                return Results.Accepted($"/api/conversations/{conversationId}", new { accepted = true });
            }

            await _ask(new NetworkChatRequest(target.CharacterId.ToString(), target.ChatId.ToString(), text, request.AuthorKind, request.AuthorPersonaId), token);
            var generated = await AppServices.Conversations.GetAsync(address, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(generated.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (kind == ConversationKind.Direct && action == "append")
        {
            var result = await AppServices.Conversations.AppendUserMessageAsync(address, request.Text ?? string.Empty, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        // Mobile uses the same next-reply control for personal and group
        // conversations. A personal continuation is not a user message: the
        // established turn runner recognises this directive and asks the
        // character to continue from the current history.
        if (kind == ConversationKind.Direct && action == "next")
        {
            var target = await AppServices.Conversations.ResolveDirectAsync(conversationId, token);
            if (string.Equals(context.Request.Query["async"], "1", StringComparison.Ordinal))
            {
                _ = StartDirectGenerationWithPreviewAsync(conversationId, new NetworkChatRequest(target.CharacterId.ToString(), target.ChatId.ToString(), "*continue*", "user"));
                return Results.Accepted($"/api/conversations/{conversationId}", new { accepted = true });
            }
            await _ask(new NetworkChatRequest(target.CharacterId.ToString(), target.ChatId.ToString(), "*continue*", "user"), token);
            var generated = await AppServices.Conversations.GetAsync(address, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(generated.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (action == "director")
        {
            var result = await AppServices.Conversations.AddDirectorEventAsync(address, request.Text ?? string.Empty, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (kind == ConversationKind.Scene && action == "send")
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return Results.BadRequest(new { error = "Введите сообщение." });
            var authorKind = request.AuthorKind?.Trim().ToLowerInvariant();
            if (authorKind == "director")
            {
                var directorResult = await AppServices.Conversations.AddDirectorEventAsync(address, request.Text, token);
                await NotifyDataChangedAsync();
                return Results.Ok(ConversationDto(directorResult.Conversation, avatarUrl: AvatarUrlForCharacter));
            }
            Guid? personaId = null;
            if (authorKind == "persona")
            {
                if (!Guid.TryParse(request.AuthorPersonaId, out var parsedPersonaId))
                    return Results.BadRequest(new { error = "Выберите персону." });
                personaId = parsedPersonaId;
            }
            var result = await AppServices.Conversations.AddSceneUserMessageAsync(address, request.Text, personaId, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (kind == ConversationKind.Scene && action is "start" or "pause" or "finish" or "next")
        {
            if (string.Equals(context.Request.Query["async"], "1", StringComparison.Ordinal) && action == "next")
            {
                _ = Task.Run(async () =>
                {
                    try { await _sceneAction(conversationId, action, CancellationToken.None); await NotifyDataChangedAsync(); }
                    catch (Exception ex) { AppLog.Write($"Conversation scene action failed: {ex}"); }
                });
                return Results.Accepted($"/api/conversations/{conversationId}", new { accepted = true });
            }

            await _sceneAction(conversationId, action, token);
            var result = await AppServices.Conversations.GetAsync(address, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (action is "pin" or "unpin")
        {
            var result = await AppServices.Conversations.SetPinnedAsync(address, action == "pin", token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        if (action == "delete")
        {
            await AppServices.Conversations.DeleteAsync(address, token);
            await NotifyDataChangedAsync();
            return Results.NoContent();
        }

        if (action == "rename")
        {
            var result = await AppServices.Conversations.RenameAsync(address, request.Text ?? string.Empty, token);
            await NotifyDataChangedAsync();
            return Results.Ok(ConversationDto(result.Conversation, avatarUrl: AvatarUrlForCharacter));
        }

        return Results.BadRequest(new { error = "Это действие недоступно для выбранного разговора." });
    }


}
