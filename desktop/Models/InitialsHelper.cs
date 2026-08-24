using System;
using System.Linq;

namespace SoulExe.Models;

/// <summary>Shared initials extraction used by CharacterProfile, SoulCharacter, SoulPersona and presentation ViewModels.</summary>
internal static class InitialsHelper
{
    public static string FromName(string? name)
    {
        var tokens = (name ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2);
        return string.Concat(tokens.Select(token => char.ToUpperInvariant(token[0])));
    }
}
