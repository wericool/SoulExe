using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Builds state-variable rows for the chat side panel from character definitions + chat values.</summary>
public static class StateVariableDisplay
{
    public static IReadOnlyList<StateVariableContextItem> Build(SoulCharacter? character, ConversationSnapshot? conversation)
    {
        if (character?.StateVariables is null || character.StateVariables.Count == 0)
            return [];

        var results = new List<StateVariableContextItem>();
        foreach (var variable in character.StateVariables.OrderBy(x => x.DisplayOrder))
        {
            var value = conversation is not null && conversation.Context.StateValues.TryGetValue(variable.Id, out var chatValue)
                ? chatValue
                : variable.DefaultValueJson;
            results.Add(new StateVariableContextItem(variable.DisplayName, variable.Key, value, variable.VariableType));
        }
        return results;
    }
}
