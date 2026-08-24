using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Character-card persistence. Conversation history is owned by ConversationService.</summary>
public sealed class CharacterLibraryService
{
    private readonly JsonDataStore _store;
    public CharacterLibraryService(JsonDataStore store) => _store = store;

    public Task<IReadOnlyList<SoulCharacter>> GetCharactersAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulCharacter>)root.Characters.OrderByDescending(value => value.IsFavorite)
            .ThenBy(value => value.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), token);

    public Task<SoulCharacter?> GetCharacterAsync(Guid characterId, CancellationToken token = default) =>
        _store.ReadAsync(root => root.Characters.FirstOrDefault(value => value.Id == characterId), token);

    public Task<SoulCharacter> CreateCharacterAsync(string name, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var now = DateTimeOffset.Now;
            var character = new SoulCharacter
            {
                Name = MakeUniqueName(root, name), Title = "Новый персонаж", Personality = "Опишите характер персонажа.",
                SystemPrompt = "Оставайся в образе персонажа и отвечай на языке пользователя.", ReplyLanguage = "Русский",
                CreatedAt = now, UpdatedAt = now
            };
            root.Characters.Add(character);
            return character;
        }, "create_character", token);

    public Task UpdateCharacterAsync(SoulCharacter draft, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var existing = GetRequired(root, draft.Id);
            existing.Name = MakeUniqueName(root, draft.Name, existing.Id);
            existing.Title = draft.Title.Trim(); existing.Description = draft.Description.Trim(); existing.Personality = draft.Personality.Trim();
            existing.PersonalityExpressionLevel = draft.PersonalityExpressionLevel is "vivid" or "subtle" ? draft.PersonalityExpressionLevel : "natural";
            existing.Scenario = draft.Scenario.Trim(); existing.SystemPrompt = draft.SystemPrompt.Trim(); existing.ReplyLanguage = (draft.ReplyLanguage ?? "").Trim();
            existing.UseRoleplayResponseFormatting = draft.UseRoleplayResponseFormatting; existing.CreatorNotes = draft.CreatorNotes.Trim();
            existing.ExampleDialogue = draft.ExampleDialogue.Trim(); existing.AvatarPath = draft.AvatarPath; existing.SelectedPersonaId = draft.SelectedPersonaId;
            existing.SelectedPromptPresetId = draft.SelectedPromptPresetId; existing.DefaultUserProfile = draft.DefaultUserProfile?.Trim() ?? "";
            existing.DefaultRelationshipContext = draft.DefaultRelationshipContext?.Trim() ?? ""; existing.FolderName = draft.FolderName.Trim();
            existing.IsFavorite = draft.IsFavorite; existing.Greetings = []; existing.LorebookIds = draft.LorebookIds;
            existing.CognitiveArchitectureEnabled = draft.CognitiveArchitectureEnabled; existing.SoulMemoryEnabled = draft.SoulMemoryEnabled;
            existing.SoulMemoryPreset = SoulMemoryPresetMode.From(draft.SoulMemoryPreset).Id;
            existing.SoulMemoryIntervalMessages = Math.Clamp(draft.SoulMemoryIntervalMessages, 1, 50);
            existing.AutoSummaryEnabled = draft.AutoSummaryEnabled; existing.AutoSummaryIntervalMessages = Math.Clamp(draft.AutoSummaryIntervalMessages, 1, 100);
            existing.StateVariables = draft.StateVariables; existing.UpdatedAt = DateTimeOffset.Now;
        }, "update_character", token);

    public Task DeleteCharacterAsync(Guid characterId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            if (root.Characters.Count <= 1) throw new InvalidOperationException("В библиотеке должен оставаться хотя бы один персонаж.");
            root.Characters.RemoveAll(value => value.Id == characterId);
            root.Conversations.RemoveAll(conversation => conversation.Participants.Any(participant => participant.CharacterId == characterId));
        }, "delete_character", token);

    public Task SetCognitiveArchitectureEnabledAsync(Guid characterId, bool enabled, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = GetRequired(root, characterId); character.CognitiveArchitectureEnabled = enabled; character.UpdatedAt = DateTimeOffset.Now;
        }, "set_character_cognitive_architecture", token);

    private static SoulCharacter GetRequired(SoulDataRoot root, Guid id) =>
        root.Characters.FirstOrDefault(value => value.Id == id) ?? throw new InvalidOperationException("Персонаж не найден.");

    private static string MakeUniqueName(SoulDataRoot root, string candidate, Guid? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Новый персонаж" : candidate.Trim();
        var name = baseName; var suffix = 2;
        while (root.Characters.Any(value => value.Id != except && string.Equals(value.Name, name, StringComparison.CurrentCultureIgnoreCase))) name = $"{baseName} {suffix++}";
        return name;
    }
}
