using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

/// <summary>Orders home-page character cards by the selected sort mode.</summary>
public static class HomeCharacterCards
{
    public static IReadOnlyList<HomeCharacterCardViewModel> Build(IEnumerable<SoulCharacter> characters, IEnumerable<ConversationSnapshot> conversations, string sortMode)
    {
        var conversationList = conversations.ToList();
        IEnumerable<SoulCharacter> ordered = sortMode switch
        {
            "count" => characters.OrderByDescending(character => CharacterActivity.MessageCount(character, conversationList))
                .ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            "created" => characters.OrderByDescending(character => character.CreatedAt)
                .ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            "name" => characters.OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => characters.OrderByDescending(character => CharacterActivity.LastActivity(character, conversationList))
                .ThenBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        return ordered.Select(character => new HomeCharacterCardViewModel(character)).ToList();
    }
}
