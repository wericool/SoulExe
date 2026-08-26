using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Creates canonical conversations for initial data and import pipelines.</summary>
public static class ConversationFactory
{
    private static readonly Guid UserParticipantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static ConversationSnapshot Personal(SoulCharacter character, string name, DateTimeOffset? createdAt = null)
    {
        var now = createdAt ?? DateTimeOffset.Now;
        return new ConversationSnapshot
        {
            Id = Guid.NewGuid(),
            Kind = ConversationKind.Direct,
            Source = ConversationSource.CharacterChat,
            Name = string.IsNullOrWhiteSpace(name) ? "Новый разговор" : name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
            Participants =
            [
                new ConversationParticipant(UserParticipantId, ConversationParticipantKind.User, "Вы", null, false, 0),
                new ConversationParticipant(Guid.NewGuid(), ConversationParticipantKind.Character, character.Name, character.Id, true, 1)
            ],
            Context = new ConversationContextSnapshot
            {
                InitialUserProfile = character.DefaultUserProfile?.Trim() ?? "",
                InitialRelationshipContext = character.DefaultRelationshipContext?.Trim() ?? "",
                SummaryDirectives = "Сохраняй факты, важные события, цели, эмоции и незавершённые темы.",
                Memory = new SoulMemoryBundle()
            }
        };
    }
}
