using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Picks default A/B participants for a new scene draft.</summary>
public static class SceneParticipantPicker
{
    public static (SoulCharacter? A, SoulCharacter? B) EnsureDefaults(
        IReadOnlyList<SoulCharacter> characters,
        SoulCharacter? currentA,
        SoulCharacter? currentB)
    {
        var a = currentA;
        var b = currentB;
        if (a is null && characters.Count > 0) a = characters[0];
        if (b is null || b.Id == a?.Id)
            b = characters.FirstOrDefault(character => character.Id != a?.Id);
        return (a, b);
    }
}
