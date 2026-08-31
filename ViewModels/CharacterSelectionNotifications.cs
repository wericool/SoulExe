namespace SoulExe.ViewModels;

/// <summary>Property names refreshed when the selected character changes.</summary>
public static class CharacterSelectionNotifications
{
    public static readonly string[] AfterReload =
    [
        nameof(MainViewModel.SelectedCharacter),
        nameof(MainViewModel.SelectedCharacterCognitiveArchitectureEnabled),
        nameof(MainViewModel.SelectedCharacterSoulMemoryEnabled),
        nameof(MainViewModel.SelectedCharacterSoulMemoryPreset),
        nameof(MainViewModel.SelectedCharacterSoulMemoryIntervalMessages),
        nameof(MainViewModel.SelectedCharacterAutoSummaryEnabled),
        nameof(MainViewModel.SelectedCharacterProactiveMessagesEnabled),
        nameof(MainViewModel.SelectedCharacterProactiveQuietHoursEnabled),
        nameof(MainViewModel.SelectedCharacterProactiveQuietHoursStart),
        nameof(MainViewModel.SelectedCharacterProactiveQuietHoursEnd),
        nameof(MainViewModel.SelectedCharacterRealisticMessagingEnabled),
        nameof(MainViewModel.SelectedCharacterAutoSummaryIntervalMessages),
        nameof(MainViewModel.SelectedCharacterCognitiveStatus),
        nameof(MainViewModel.SelectedCharacterPersonaId),
        nameof(MainViewModel.IsSelectedCharacterPersonaEnabled),
        nameof(MainViewModel.SelectedCharacterPersonaDescription)
    ];
}
