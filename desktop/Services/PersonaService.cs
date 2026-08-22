using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Stores reusable user personas. A persona is a persistent description of the user that can be
/// assigned to one or more character cards and injected into their chat prompt.
/// </summary>
public sealed class PersonaService
{
    private readonly JsonDataStore _store;

    public PersonaService(JsonDataStore store) => _store = store;

    public Task<IReadOnlyList<SoulPersona>> GetPersonasAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulPersona>)root.Personas
            .OrderByDescending(persona => persona.UpdatedAt)
            .ThenBy(persona => persona.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList(), token);

    public Task<SoulPersona> CreateAsync(string name, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var now = DateTimeOffset.Now;
            var persona = new SoulPersona
            {
                Name = MakeUniqueName(root, name),
                CreatedAt = now,
                UpdatedAt = now
            };
            root.Personas.Add(persona);
            return persona;
        }, "create_persona", token);

    public Task UpdateAsync(SoulPersona draft, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var existing = root.Personas.FirstOrDefault(persona => persona.Id == draft.Id)
                ?? throw new InvalidOperationException("Персона не найдена.");
            existing.Name = MakeUniqueName(root, draft.Name, existing.Id);
            existing.Description = draft.Description?.Trim() ?? "";
            existing.PromptText = draft.PromptText?.Trim() ?? "";
            existing.AvatarPath = draft.AvatarPath?.Trim() ?? "";
            existing.UpdatedAt = DateTimeOffset.Now;
        }, "update_persona", token);

    public Task DeleteAsync(Guid personaId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            if (root.Personas.RemoveAll(persona => persona.Id == personaId) == 0)
                throw new InvalidOperationException("Персона не найдена.");

            var now = DateTimeOffset.Now;
            foreach (var character in root.Characters.Where(character => character.SelectedPersonaId == personaId))
            {
                character.SelectedPersonaId = null;
                character.UpdatedAt = now;
            }
        }, "delete_persona", token);

    private static string MakeUniqueName(SoulDataRoot root, string? candidate, Guid? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Новая персона" : candidate.Trim();
        var name = baseName;
        var suffix = 2;
        while (root.Personas.Any(persona => persona.Id != except && string.Equals(persona.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }
}
