using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private void RefreshStateVariableValues()
    {
        StateVariableValues.Clear();
        foreach (var item in StateVariableDisplay.Build(SelectedCharacter, SelectedPersonalConversation?.Conversation))
            StateVariableValues.Add(item);
    }
    private void RefreshLorebookBindingFlag()
    {
        var bound = SelectedLorebook is not null && SelectedCharacter?.LorebookIds.Contains(SelectedLorebook.Id) == true;
        if (_isSelectedLorebookBound == bound) return;
        _isSelectedLorebookBound = bound;
        OnPropertyChanged(nameof(IsSelectedLorebookBound));
    }
    private async Task ReloadLorebooksAsync(Guid? selectId = null)
    {
        var books = await _lorebooks.GetLorebooksAsync();
        Lorebooks.Clear();
        foreach (var book in books) Lorebooks.Add(book);
        // Присваивание через сеттер обязательно обновляет доступность команд сохранения, добавления и удаления записей.
        SelectedLorebook = selectId is null ? Lorebooks.FirstOrDefault() : Lorebooks.FirstOrDefault(x => x.Id == selectId);
        OnPropertyChanged(nameof(IsSelectedLorebookBound));
    }
    private async Task ReloadPersonasAsync(Guid? selectId = null)
    {
        var currentId = selectId ?? SelectedPersona?.Id;
        var personas = await _personas.GetPersonasAsync();
        Personas.Clear();
        foreach (var persona in personas) Personas.Add(persona);
        var selectedAuthorKey = ComposerAuthor.Key;
        ComposerAuthors.Clear();
        ComposerAuthors.Add(ComposerAuthorOption.User);
        foreach (var persona in personas)
            ComposerAuthors.Add(new ComposerAuthorOption($"persona:{persona.Id}", persona.Name, SoulMessageAuthorKind.Persona, persona.Id, persona.AvatarPath));
        ComposerAuthors.Add(ComposerAuthorOption.Director);
        ComposerAuthor = ComposerAuthors.FirstOrDefault(value => value.Key == selectedAuthorKey) ?? ComposerAuthorOption.User;
        SelectedPersona = currentId is null ? Personas.FirstOrDefault() : Personas.FirstOrDefault(persona => persona.Id == currentId);
        OnPropertyChanged(nameof(HasPersonas));
        OnPropertyChanged(nameof(SelectedCharacterPersonaId));
        OnPropertyChanged(nameof(IsSelectedCharacterPersonaEnabled));
        OnPropertyChanged(nameof(SelectedCharacterPersonaDescription));
    }
    private void OpenLibraryLoreEditor(SoulLorebook? lorebook)
    {
        if (lorebook is null) return;
        LibraryTab = "lore";
        SelectedLorebook = lorebook;
        IsLibraryLoreEditorOpen = true;
    }
    private async Task AddLorebookAsync()
    {
        try
        {
            IsBusy = true;
            var book = await _lorebooks.CreateAsync($"Лорбук {Lorebooks.Count + 1}");
            await ReloadLorebooksAsync(book.Id);
            IsLibraryLoreEditorOpen = true;
            Status = $"Создан лорбук «{book.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать лорбук", ex); }
        finally { IsBusy = false; }
    }
    private async Task SaveLorebookAsync()
    {
        if (SelectedLorebook is null) return;
        try
        {
            await _lorebooks.UpdateAsync(SelectedLorebook);
            Status = "Лорбук сохранён.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить лорбук", ex); }
    }
    private async Task DeleteLorebookAsync(SoulLorebook? book)
    {
        if (book is null) return;
        await _lorebooks.DeleteAsync(book.Id);
        if (SelectedLorebook?.Id == book.Id) { IsLibraryLoreEditorOpen = false; SelectedLorebook = null; }
        await ReloadLorebooksAsync();
        Status = $"Лорбук «{book.Name}» удалён.";
    }
    private async Task DeleteLoreEntryAsync(SoulLoreEntry? entry)
    {
        if (SelectedLorebook is null || entry is null) return;
        try
        {
            IsBusy = true;
            await _lorebooks.DeleteEntryAsync(SelectedLorebook.Id, entry.Id);
            await ReloadLorebooksAsync(SelectedLorebook.Id);
            Status = "Запись лорбука удалена.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить запись лорбука", ex); }
        finally { IsBusy = false; }
    }
    private async Task AddLoreEntryAsync()
    {
        if (SelectedLorebook is null) return;
        try
        {
            await _lorebooks.AddEntryAsync(SelectedLorebook.Id);
            await ReloadLorebooksAsync(SelectedLorebook.Id);
            Status = "Добавлена запись лорбука.";
        }
        catch (Exception ex) { HandleError("Не удалось добавить запись лорбука", ex); }
    }
    private async Task SetLorebookBindingAsync(bool bind)
    {
        if (SelectedCharacter is null || SelectedLorebook is null) return;
        try
        {
            await _lorebooks.BindAsync(SelectedCharacter.Id, SelectedLorebook.Id, bind);
            if (bind && !SelectedCharacter.LorebookIds.Contains(SelectedLorebook.Id)) SelectedCharacter.LorebookIds.Add(SelectedLorebook.Id);
            if (!bind) SelectedCharacter.LorebookIds.RemoveAll(x => x == SelectedLorebook.Id);
            Status = bind ? "Лорбук привязан к персонажу." : "Лорбук отключён для персонажа.";
        }
        catch (Exception ex) { HandleError("Не удалось изменить привязку лорбука", ex); }
    }
    private void OpenPersonaEditor(SoulPersona? persona)
    {
        if (persona is null) return;
        LibraryTab = "personas";
        SelectedPersona = persona;
        IsPersonaEditorOpen = true;
    }
    private async Task AddPersonaAsync()
    {
        try
        {
            IsBusy = true;
            var persona = await _personas.CreateAsync($"Персона {Personas.Count + 1}");
            await ReloadPersonasAsync(persona.Id);
            LibraryTab = "personas";
            IsPersonaEditorOpen = true;
            Status = $"Создана персона «{persona.Name}».";
        }
        catch (Exception ex) { HandleError("Не удалось создать персону", ex); }
        finally { IsBusy = false; }
    }
    private async Task SavePersonaAsync()
    {
        if (SelectedPersona is null) return;
        try
        {
            IsBusy = true;
            var personaId = SelectedPersona.Id;
            await _personas.UpdateAsync(SelectedPersona);
            await ReloadPersonasAsync(personaId);
            Status = "Персона сохранена.";
        }
        catch (Exception ex) { HandleError("Не удалось сохранить персону", ex); }
        finally { IsBusy = false; }
    }
    private void ConfirmDeletePersona(SoulPersona? persona)
    {
        if (persona is not null) PersonaPendingDeletion = persona;
    }
    private async Task DeletePersonaAsync()
    {
        var persona = PersonaPendingDeletion;
        if (persona is null) return;
        try
        {
            IsBusy = true;
            var selectedCharacterId = SelectedCharacter?.Id;
            PersonaPendingDeletion = null;
            await _personas.DeleteAsync(persona.Id);
            if (SelectedPersona?.Id == persona.Id)
            {
                IsPersonaEditorOpen = false;
                SelectedPersona = null;
            }
            await ReloadPersonasAsync();
            await ReloadCharactersAsync(selectedCharacterId);
            Status = $"Персона «{persona.Name}» удалена и отключена у связанных персонажей.";
        }
        catch (Exception ex) { HandleError("Не удалось удалить персону", ex); }
        finally { IsBusy = false; }
    }
    private void ChoosePersonaAvatar()
    {
        if (SelectedPersona is null) return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SelectedPersona.AvatarPath = LocalMediaStore.CopyAvatar(dialog.FileName, SelectedPersona.Id, AppServices.Paths.AvatarDirectory, "persona_");
            _ = SavePersonaAsync();
            OnPropertyChanged(nameof(SelectedPersona));
        }
        catch (Exception ex) { HandleError("Не удалось сохранить фото-аватар персоны", ex); }
    }
}
