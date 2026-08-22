using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class LorebookService
{
    private readonly JsonDataStore _store;
    public LorebookService(JsonDataStore store) => _store = store;

    public Task<IReadOnlyList<SoulLorebook>> GetLorebooksAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulLorebook>)root.Lorebooks.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), token);

    public Task<SoulLorebook> CreateAsync(string name, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var book = new SoulLorebook { Name = MakeUniqueName(root, name) };
            root.Lorebooks.Add(book);
            return book;
        }, "create_lorebook", token);

    public Task UpdateAsync(SoulLorebook draft, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var target = root.Lorebooks.FirstOrDefault(x => x.Id == draft.Id) ?? throw new InvalidOperationException("Лорбук не найден.");
            target.Name = MakeUniqueName(root, draft.Name, draft.Id);
            target.Description = draft.Description;
            target.Entries = draft.Entries;
            target.UpdatedAt = DateTimeOffset.Now;
        }, "update_lorebook", token);

    public Task DeleteAsync(Guid lorebookId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            root.Lorebooks.RemoveAll(x => x.Id == lorebookId);
            foreach (var character in root.Characters) character.LorebookIds.RemoveAll(x => x == lorebookId);
        }, "delete_lorebook", token);

    public Task BindAsync(Guid characterId, Guid lorebookId, bool bind, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var character = root.Characters.FirstOrDefault(x => x.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
            if (root.Lorebooks.All(x => x.Id != lorebookId)) throw new InvalidOperationException("Лорбук не найден.");
            if (bind && !character.LorebookIds.Contains(lorebookId)) character.LorebookIds.Add(lorebookId);
            if (!bind) character.LorebookIds.RemoveAll(x => x == lorebookId);
            character.UpdatedAt = DateTimeOffset.Now;
        }, bind ? "bind_lorebook" : "unbind_lorebook", token);

    public Task DeleteEntryAsync(Guid lorebookId, Guid entryId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var book = root.Lorebooks.FirstOrDefault(x => x.Id == lorebookId) ?? throw new InvalidOperationException("Лорбук не найден.");
            book.Entries.RemoveAll(entry => entry.Id == entryId);
            for (var index = 0; index < book.Entries.Count; index++) book.Entries[index].InsertionOrder = index;
            book.UpdatedAt = DateTimeOffset.Now;
        }, "delete_lore_entry", token);

    public Task<SoulLoreEntry> AddEntryAsync(Guid lorebookId, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var book = root.Lorebooks.FirstOrDefault(x => x.Id == lorebookId) ?? throw new InvalidOperationException("Лорбук не найден.");
            var entry = new SoulLoreEntry { Name = "Новая запись", TriggerMode = "always", InsertionOrder = book.Entries.Count };
            book.Entries.Add(entry);
            book.UpdatedAt = DateTimeOffset.Now;
            return entry;
        }, "add_lore_entry", token);

    private static string MakeUniqueName(SoulDataRoot root, string candidate, Guid? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(candidate) ? "Новый лорбук" : candidate.Trim();
        var name = baseName;
        var suffix = 2;
        while (root.Lorebooks.Any(x => x.Id != except && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            name = $"{baseName} {suffix++}";
        return name;
    }
}
