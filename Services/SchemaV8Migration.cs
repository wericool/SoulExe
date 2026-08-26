using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Explicit reader for the retired v8 wire format. Do not deserialize it as SoulDataRoot.</summary>
public static class SchemaV8Migration
{
    public static SoulDataRoot ParseAndMigrate(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("SchemaVersion", out var version) || version.GetInt32() != 8)
            throw new InvalidDataException("Ожидался файл schema v8.");

        var legacy = JsonSerializer.Deserialize<LegacyV8Root>(json, SerializerOptions)
            ?? throw new InvalidDataException("Файл schema v8 пуст.");
        var migrated = new SoulDataRoot
        {
            SchemaVersion = 10,
            Preferences = legacy.Preferences ?? new AppPreferences(),
            Personas = legacy.Personas ?? [], PromptPresets = legacy.PromptPresets ?? [], Characters = legacy.Characters ?? [],
            Lorebooks = legacy.Lorebooks ?? [], Models = legacy.Models ?? [], ImportRuns = legacy.ImportRuns ?? []
        };
        migrated.Preferences.MobileAccessPassword = "";

        foreach (var chat in legacy.Chats ?? [])
            migrated.Conversations.Add(ToConversation(chat, ConversationKind.Direct));
        foreach (var scene in legacy.Scenes ?? [])
            migrated.Conversations.Add(ToConversation(scene, ConversationKind.Scene));

        Validate(migrated, (legacy.Chats?.Count ?? 0) + (legacy.Scenes?.Count ?? 0),
            (legacy.Chats ?? []).Concat(legacy.Scenes ?? []).Sum(item => item.Messages?.Count ?? 0));
        return migrated;
    }

    public static void Validate(SoulDataRoot root, int expectedConversations, int expectedMessages)
    {
        if (root.SchemaVersion != 10 || root.Conversations.Count != expectedConversations || root.Conversations.Sum(x => x.Messages.Count) != expectedMessages)
            throw new InvalidDataException("Проверка полноты миграции не пройдена.");
        if (root.Conversations.Any(x => x.Id == Guid.Empty) || root.Conversations.Select(x => x.Id).Distinct().Count() != root.Conversations.Count)
            throw new InvalidDataException("Миграция обнаружила отсутствующие или повторяющиеся идентификаторы разговоров.");
        if (root.Conversations.Any(x => x.Messages.Any(m => m.Id == Guid.Empty)) || !string.IsNullOrEmpty(root.Preferences.MobileAccessPassword))
            throw new InvalidDataException("Миграция не прошла проверку безопасности или идентификаторов сообщений.");
    }

    private static ConversationSnapshot ToConversation(LegacyV8Conversation source, ConversationKind kind)
    {
        if (source.Id == Guid.Empty) throw new InvalidDataException("В legacy-разговоре отсутствует Id.");
        var messages = source.Messages ?? [];
        return new ConversationSnapshot
        {
            Id = source.Id, Kind = kind, Source = kind == ConversationKind.Scene ? ConversationSource.RootScene : ConversationSource.CharacterChat,
            Name = source.Name ?? "", IsPinned = source.IsPinned, IsArchived = source.IsArchived, SummaryText = source.SummaryText ?? "",
            LastSummarizedSequence = source.LastSummarizedSequence, CreatedAt = source.CreatedAt, UpdatedAt = source.UpdatedAt,
            Participants = source.Participants ?? [], Context = source.Context ?? new ConversationContextSnapshot(), TurnState = source.TurnState,
            Messages = messages.Select(ToMessage).ToList()
        };
    }

    private static ConversationMessageSnapshot ToMessage(LegacyV8Message source)
    {
        if (source.Id == Guid.Empty) throw new InvalidDataException("В legacy-сообщении отсутствует Id.");
        return new ConversationMessageSnapshot { Id = source.Id, SequenceNumber = source.SequenceNumber, Kind = source.Kind, AuthorParticipantId = source.AuthorParticipantId, AuthorKind = source.AuthorKind, AuthorPersonaId = source.AuthorPersonaId, AuthorAvatarPath = source.AuthorAvatarPath ?? "", AuthorName = source.AuthorName ?? "", Content = source.Content ?? "", CreatedAt = source.CreatedAt, EditedAt = source.EditedAt, SelectedVariantId = source.SelectedVariantId, Variants = source.Variants ?? [], Attachments = source.Attachments ?? [] };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class LegacyV8Root
    {
        public AppPreferences? Preferences { get; set; }
        public List<SoulPersona>? Personas { get; set; } public List<SoulPromptPreset>? PromptPresets { get; set; }
        public List<SoulCharacter>? Characters { get; set; } public List<SoulLorebook>? Lorebooks { get; set; }
        public List<SoulModelInstallation>? Models { get; set; } public List<SoulImportRun>? ImportRuns { get; set; }
        public List<LegacyV8Conversation>? Chats { get; set; } public List<LegacyV8Conversation>? Scenes { get; set; }
    }
    private sealed class LegacyV8Conversation
    {
        public Guid Id { get; set; } public string? Name { get; set; } public bool IsPinned { get; set; } public bool IsArchived { get; set; }
        public string? SummaryText { get; set; } public int LastSummarizedSequence { get; set; } public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset UpdatedAt { get; set; }
        public List<ConversationParticipant>? Participants { get; set; } public List<LegacyV8Message>? Messages { get; set; }
        public ConversationContextSnapshot? Context { get; set; } public ConversationTurnState? TurnState { get; set; }
    }
    private sealed class LegacyV8Message
    {
        public Guid Id { get; set; } public int SequenceNumber { get; set; } public ConversationMessageKind Kind { get; set; }
        public Guid? AuthorParticipantId { get; set; } public SoulMessageAuthorKind AuthorKind { get; set; } public Guid? AuthorPersonaId { get; set; }
        public string? AuthorAvatarPath { get; set; } public string? AuthorName { get; set; } public string? Content { get; set; }
        public DateTimeOffset CreatedAt { get; set; } public DateTimeOffset? EditedAt { get; set; } public Guid? SelectedVariantId { get; set; }
        public List<ConversationMessageVariantSnapshot>? Variants { get; set; } public List<ConversationAttachmentSnapshot>? Attachments { get; set; }
    }
}
