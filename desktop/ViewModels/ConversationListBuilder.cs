using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>
/// Builds ordered conversation / chat list entries from characters and scenes.
/// UI collections stay in MainViewModel; pure filtering and ordering live here.
/// </summary>
public static class ConversationListBuilder
{
    public static IReadOnlyList<ChatListItemViewModel> BuildChatListItems(
        IEnumerable<SoulCharacter> characters,
        IEnumerable<ConversationSnapshot> conversations,
        string? searchQuery,
        string sortMode)
    {
        var characterById = characters.Where(character => character is not null).ToDictionary(character => character.Id);
        var entries = conversations.Where(conversation => conversation.Mode == ConversationMode.Personal && !conversation.IsArchived)
            .Select(conversation => (Conversation: conversation, CharacterId: conversation.Participants
                .Where(value => value.Kind == ConversationParticipantKind.Character).OrderBy(value => value.SortOrder)
                .Select(value => value.CharacterId).FirstOrDefault()))
            .Where(value => value.CharacterId is not null && characterById.ContainsKey(value.CharacterId.Value))
            .Select(value => new ChatListItemViewModel(characterById[value.CharacterId!.Value], value.Conversation));

        var query = (searchQuery ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(query))
            entries = entries.Where(item => item.MatchesSearch(query));

        var ordered = sortMode == "name"
            ? entries.OrderByDescending(item => item.IsPinned)
                .ThenBy(item => item.CharacterName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChatName, StringComparer.CurrentCultureIgnoreCase)
            : entries.OrderByDescending(item => item.IsPinned)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.CharacterName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.ChatName, StringComparer.CurrentCultureIgnoreCase);

        return ordered.ToList();
    }

    public static IReadOnlyList<ConversationListItemViewModel> BuildConversationItems(
        IEnumerable<SoulCharacter> characters,
        IEnumerable<ConversationSnapshot> conversations,
        string? searchQuery,
        string sortMode)
    {
        var characterList = characters.Where(c => c is not null).ToList();
        var entries = new List<ConversationListItemViewModel>();
        foreach (var conversation in conversations.Where(value => !value.IsArchived))
        {
            var participants = conversation.Participants
                .Where(value => value.Kind == ConversationParticipantKind.Character && value.CharacterId is not null)
                .OrderBy(value => value.SortOrder).ToList();
            var first = participants.Count > 0 ? characterList.FirstOrDefault(value => value.Id == participants[0].CharacterId) : null;
            if (first is null) continue;
            if (conversation.Mode == ConversationMode.Personal)
            {
                entries.Add(ConversationListItemViewModel.FromPersonal(first, conversation));
                continue;
            }
            var second = participants.Count > 1 ? characterList.FirstOrDefault(value => value.Id == participants[1].CharacterId) : null;
            entries.Add(ConversationListItemViewModel.FromGroup(conversation, first, second));
        }

        var query = (searchQuery ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(query))
            entries = entries.Where(item => item.MatchesSearch(query)).ToList();

        var ordered = sortMode == "name"
            ? entries.OrderByDescending(item => item.IsPinned)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(item => item.IsScene ? 1 : 0)
            : entries.OrderByDescending(item => item.IsPinned)
                .ThenByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase);

        return ordered.ToList();
    }

    public static ConversationListItemViewModel? RestoreSelection(
        IReadOnlyList<ConversationListItemViewModel> items,
        Guid selectedId,
        bool selectedWasScene,
        Guid? currentSceneId,
        Guid? currentChatId)
    {
        var selectedKind = selectedWasScene ? "scene" : "chat";
        return items.FirstOrDefault(item => item.Id == selectedId && (item.IsScene ? "scene" : "chat") == selectedKind)
            ?? items.FirstOrDefault(item => item.IsScene && item.Id == currentSceneId)
            ?? items.FirstOrDefault(item => !item.IsScene && item.Id == currentChatId);
    }
}
