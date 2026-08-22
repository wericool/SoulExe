using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class CharacterLibraryService
{
    private readonly JsonDataStore _store;

    public CharacterLibraryService(JsonDataStore store) => _store = store;

    public Task<IReadOnlyList<SoulCharacter>> GetCharactersAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulCharacter>)root.Characters
            .OrderByDescending(x => x.IsFavorite)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList(), token);

    public Task<SoulCharacter?> GetCharacterAsync(Guid characterId, CancellationToken token = default) =>
        _store.ReadAsync(root => root.Characters.FirstOrDefault(x => x.Id == characterId), token);

    public Task<SoulCharacter> CreateCharacterAsync(string name, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var now = DateTimeOffset.Now;
            var character = new SoulCharacter
            {
                Name = MakeUniqueName(root, name),
                Title = "Новый персонаж",
                Personality = "Опишите характер персонажа.",
                SystemPrompt = "Оставайся в образе персонажа и отвечай на языке пользователя.",
                CreatedAt = now,
                UpdatedAt = now,
                Greetings = [new SoulGreeting { Text = "Привет! Я готов к разговору.", IsPrimary = true, Position = 0 }]
            };
            // Новый персонаж создаётся только как карточка. Диалог появляется исключительно по явной команде «Новый чат».
            root.Characters.Add(character);
            return character;
        }, "create_character", token);

    public Task UpdateCharacterAsync(SoulCharacter draft, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var existing = GetRequired(root, draft.Id);
            existing.Name = MakeUniqueName(root, draft.Name, existing.Id);
            existing.Title = draft.Title.Trim();
            existing.Description = draft.Description.Trim();
            existing.Personality = draft.Personality.Trim();
            existing.PersonalityExpressionLevel = draft.PersonalityExpressionLevel is "vivid" or "subtle" ? draft.PersonalityExpressionLevel : "natural";
            existing.Scenario = draft.Scenario.Trim();
            existing.SystemPrompt = draft.SystemPrompt.Trim();
            existing.UseRoleplayResponseFormatting = draft.UseRoleplayResponseFormatting;
            existing.CreatorNotes = draft.CreatorNotes.Trim();
            existing.ExampleDialogue = draft.ExampleDialogue.Trim();
            existing.AvatarPath = draft.AvatarPath;
            existing.SelectedPersonaId = draft.SelectedPersonaId;
            existing.SelectedPromptPresetId = draft.SelectedPromptPresetId;
            existing.DefaultUserProfile = draft.DefaultUserProfile?.Trim() ?? "";
            existing.DefaultRelationshipContext = draft.DefaultRelationshipContext?.Trim() ?? "";
            existing.FolderName = draft.FolderName.Trim();
            existing.IsFavorite = draft.IsFavorite;
            existing.Greetings = draft.Greetings;
            existing.LorebookIds = draft.LorebookIds;
            existing.CognitiveArchitectureEnabled = draft.CognitiveArchitectureEnabled;
            existing.SoulMemoryEnabled = draft.SoulMemoryEnabled;
            existing.SoulMemoryPreset = SoulMemoryPresetMode.From(draft.SoulMemoryPreset).Id;
            existing.SoulMemoryIntervalMessages = Math.Clamp(draft.SoulMemoryIntervalMessages, 1, 50);
            existing.AutoSummaryEnabled = draft.AutoSummaryEnabled;
            existing.AutoSummaryIntervalMessages = Math.Clamp(draft.AutoSummaryIntervalMessages, 1, 100);
            existing.StateVariables = draft.StateVariables;
            existing.UpdatedAt = DateTimeOffset.Now;
        }, "update_character", token);

    public Task DeleteCharacterAsync(Guid characterId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            if (root.Characters.Count <= 1)
                throw new InvalidOperationException("В библиотеке должен оставаться хотя бы один персонаж.");
            root.Characters.RemoveAll(x => x.Id == characterId);
        }, "delete_character", token);

    public Task<SoulChat> CreateChatAsync(Guid characterId, string name, bool copyCardOnly = true, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId);
            var now = DateTimeOffset.Now;
            var chat = new SoulChat
            {
                Name = MakeUniqueChatName(character, name),
                CreatedAt = now,
                UpdatedAt = now,
                SummaryDirectives = "Сохраняй факты, важные события, цели, эмоции и незавершённые темы.",
                InitialUserProfile = character.DefaultUserProfile?.Trim() ?? "",
                InitialRelationshipContext = character.DefaultRelationshipContext?.Trim() ?? ""
            };
            character.Chats.Add(chat);
            character.CurrentChatId = chat.Id;
            character.UpdatedAt = now;
            return chat;
        }, "create_chat", token);

    public Task UpdateChatStartingContextAsync(Guid characterId, Guid chatId, string? userProfile, string? relationshipContext, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            chat.InitialUserProfile = userProfile?.Trim() ?? "";
            chat.InitialRelationshipContext = relationshipContext?.Trim() ?? "";
            TouchChat(root, characterId, chatId);
        }, "update_chat_starting_context", token);

    public Task SelectChatAsync(Guid characterId, Guid chatId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId);
            if (character.Chats.All(x => x.Id != chatId)) throw new InvalidOperationException("Чат не найден.");
            character.CurrentChatId = chatId;
            character.UpdatedAt = DateTimeOffset.Now;
        }, "select_chat", token);

    public Task RenameChatAsync(Guid characterId, Guid chatId, string name, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId);
            var chat = GetChat(root, characterId, chatId);
            var requested = string.IsNullOrWhiteSpace(name) ? chat.Name : name.Trim();
            var baseName = requested;
            var uniqueName = baseName;
            var suffix = 2;
            while (character.Chats.Any(x => x.Id != chatId && string.Equals(x.Name, uniqueName, StringComparison.CurrentCultureIgnoreCase)))
                uniqueName = $"{baseName} {suffix++}";
            chat.Name = uniqueName;
            chat.UpdatedAt = DateTimeOffset.Now;
            character.UpdatedAt = DateTimeOffset.Now;
        }, "rename_chat", token);

    public Task SetCognitiveArchitectureEnabledAsync(Guid characterId, bool enabled, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId);
            character.CognitiveArchitectureEnabled = enabled;
            character.UpdatedAt = DateTimeOffset.Now;
        }, "set_character_cognitive_architecture", token);

    public Task ArchiveChatAsync(Guid characterId, Guid chatId, bool archive, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            chat.IsArchived = archive;
            chat.UpdatedAt = DateTimeOffset.Now;
        }, archive ? "archive_chat" : "restore_chat", token);

    public Task SetChatPinnedAsync(Guid characterId, Guid chatId, bool pinned, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            chat.IsPinned = pinned;
        }, pinned ? "pin_chat" : "unpin_chat", token);

    public Task DeleteChatAsync(Guid characterId, Guid chatId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId);
            character.Chats.RemoveAll(x => x.Id == chatId);
            if (character.CurrentChatId == chatId)
                character.CurrentChatId = character.Chats.FirstOrDefault(x => !x.IsArchived)?.Id;
            character.UpdatedAt = DateTimeOffset.Now;
        }, "delete_chat", token);

    public Task<SoulMessage> AddMessageAsync(Guid characterId, Guid chatId, SoulMessageRole role, string authorName, string content, string label = "Основной", CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            var now = DateTimeOffset.Now;
            var variant = new SoulMessageVariant { Label = label, Content = content, CreatedAt = now };
            var message = new SoulMessage
            {
                Role = role,
                AuthorName = authorName,
                SequenceNumber = chat.Messages.Count == 0 ? 1 : chat.Messages.Max(x => x.SequenceNumber) + 1,
                CurrentVariantId = variant.Id,
                Variants = [variant],
                CreatedAt = now
            };
            chat.Messages.Add(message);
            chat.UpdatedAt = now;
            return message;
        }, "add_message", token);

    public Task<SoulMessageVariant> AddResponseVariantAsync(Guid characterId, Guid chatId, Guid messageId, string content, string label = "Регенерация", CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var message = GetMessage(root, characterId, chatId, messageId);
            if (message.Role != SoulMessageRole.Assistant) throw new InvalidOperationException("Варианты ответа доступны только для ассистента.");
            var variant = new SoulMessageVariant { Label = label, Content = content, CreatedAt = DateTimeOffset.Now };
            message.Variants.Add(variant);
            message.CurrentVariantId = variant.Id;
            message.EditedAt = DateTimeOffset.Now;
            TouchChat(root, characterId, chatId);
            return variant;
        }, "add_response_variant", token);

    public Task SelectResponseVariantAsync(Guid characterId, Guid chatId, Guid messageId, Guid variantId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var message = GetMessage(root, characterId, chatId, messageId);
            if (message.Variants.All(x => x.Id != variantId)) throw new InvalidOperationException("Вариант ответа не найден.");
            message.CurrentVariantId = variantId;
            message.EditedAt = DateTimeOffset.Now;
            TouchChat(root, characterId, chatId);
        }, "select_response_variant", token);

    public Task EditMessageAsync(Guid characterId, Guid chatId, Guid messageId, string content, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var message = GetMessage(root, characterId, chatId, messageId);
            var variant = message.Variants.FirstOrDefault(x => x.Id == message.CurrentVariantId)
                          ?? throw new InvalidOperationException("Текущий вариант сообщения не найден.");
            variant.Content = content;
            message.EditedAt = DateTimeOffset.Now;
            var chat = GetChat(root, characterId, chatId);
            chat.SummaryText = "";
            chat.LastSummarizedSequence = 0;
            chat.Memory = new SoulMemoryBundle();
            TouchChat(root, characterId, chatId);
        }, "edit_message", token);

    public Task DeleteMessageAsync(Guid characterId, Guid chatId, Guid messageId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            chat.Messages.RemoveAll(x => x.Id == messageId);
            chat.SummaryText = "";
            chat.LastSummarizedSequence = 0;
            chat.Memory = new SoulMemoryBundle();
            Renumber(chat);
            TouchChat(root, characterId, chatId);
        }, "delete_message", token);

    /// <summary>
    /// Keeps the selected message and removes every later message, matching the original
    /// "Continue from this message" branch behaviour. Cognitive context is reset because
    /// summary and extracted memory may contain information from the discarded future.
    /// </summary>
    public Task<int> TruncateChatAfterMessageAsync(Guid characterId, Guid chatId, Guid messageId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var chat = GetChat(root, characterId, chatId);
            var pivot = chat.Messages.FirstOrDefault(x => x.Id == messageId)
                        ?? throw new InvalidOperationException("Сообщение не найдено.");
            var removed = chat.Messages.RemoveAll(x => x.SequenceNumber > pivot.SequenceNumber);
            if (removed > 0)
            {
                chat.SummaryText = "";
                chat.LastSummarizedSequence = 0;
                chat.Memory = new SoulMemoryBundle();
            }
            Renumber(chat);
            TouchChat(root, characterId, chatId);
            return removed;
        }, "truncate_chat_branch", token);

    private static SoulCharacter GetRequired(SoulDataRoot root, Guid id) =>
        root.Characters.FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Персонаж не найден.");

    private static SoulChat GetChat(SoulDataRoot root, Guid characterId, Guid chatId) =>
        GetRequired(root, characterId).Chats.FirstOrDefault(x => x.Id == chatId) ?? throw new InvalidOperationException("Чат не найден.");

    private static SoulMessage GetMessage(SoulDataRoot root, Guid characterId, Guid chatId, Guid messageId) =>
        GetChat(root, characterId, chatId).Messages.FirstOrDefault(x => x.Id == messageId) ?? throw new InvalidOperationException("Сообщение не найдено.");

    private static void TouchChat(SoulDataRoot root, Guid characterId, Guid chatId)
    {
        var chat = GetChat(root, characterId, chatId);
        chat.UpdatedAt = DateTimeOffset.Now;
        GetRequired(root, characterId).UpdatedAt = DateTimeOffset.Now;
    }

    private static void Renumber(SoulChat chat)
    {
        foreach (var (message, index) in chat.Messages.OrderBy(x => x.SequenceNumber).Select((x, i) => (x, i)))
            message.SequenceNumber = index + 1;
    }

    private static string MakeUniqueName(SoulDataRoot root, string candidate, Guid? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Новый персонаж" : candidate.Trim();
        var name = baseName;
        var suffix = 2;
        while (root.Characters.Any(x => x.Id != except && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }

    private static string MakeUniqueChatName(SoulCharacter character, string candidate)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Новый чат" : candidate.Trim();
        var name = baseName;
        var suffix = 2;
        while (character.Chats.Any(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }
}
