using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Builds scene message view-models with speaker avatars resolved from the character list.</summary>
public static class SceneMessageTimeline
{
    public static IReadOnlyList<SceneMessageViewModel> Build(
        ConversationSnapshot conversation,
        IEnumerable<SoulCharacter> characters)
    {
        var characterList = characters as IList<SoulCharacter> ?? characters.ToList();
        var firstCharacterId = conversation.Participants
            .Where(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId is not null)
            .OrderBy(value => value.SortOrder)
            .Select(value => value.CharacterId!.Value)
            .FirstOrDefault();
        return conversation.Messages
            .OrderBy(message => message.SequenceNumber)
            .Select(message =>
            {
                var speakerId = conversation.FindParticipant(message.AuthorParticipantId)?.CharacterId;
                var avatarPath = speakerId is Guid characterId
                    ? characterList.FirstOrDefault(character => character.Id == characterId)?.AvatarPath
                    : null;
                return new SceneMessageViewModel(conversation, message, firstCharacterId, avatarPath);
            })
            .ToList();
    }
}
