using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Shared metrics for character list sorting and home cards.</summary>
public static class CharacterActivity
{
    public static DateTimeOffset LastActivity(SoulCharacter character, IEnumerable<ConversationSnapshot> conversations) =>
        conversations.Where(conversation => !conversation.IsArchived && conversation.Participants.Any(participant => participant.CharacterId == character.Id))
            .SelectMany(conversation => conversation.Messages)
            .Select(message => message.CreatedAt)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

    public static int MessageCount(SoulCharacter character, IEnumerable<ConversationSnapshot> conversations) =>
        conversations.Where(conversation => !conversation.IsArchived && conversation.Participants.Any(participant => participant.CharacterId == character.Id))
            .Sum(conversation => conversation.Messages.Count);
}
