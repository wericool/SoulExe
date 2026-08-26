using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private string _appLanguage = LocalizationService.DefaultLanguage;

    /// <summary>UI language ("ru" / "en"). Persisted in AppPreferences.Language; applied instantly.</summary>
    public string AppLanguage
    {
        get => _appLanguage;
        set
        {
            var language = LocalizationService.Normalize(value);
            if (string.Equals(_appLanguage, language, StringComparison.OrdinalIgnoreCase)) return;
            _appLanguage = language;
            LocalizationResourceLoader.Apply(language);
            OnPropertyChanged(nameof(AppLanguage));
            _ = PersistLanguageAsync(language);
        }
    }

    private void InitializeLocalization(string? storedLanguage)
    {
        _appLanguage = LocalizationService.Normalize(storedLanguage);
        LocalizationResourceLoader.Apply(_appLanguage);
        LocalizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => UiThread.BeginInvoke(() =>
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(PageSubtitle));
    });

    private async Task PersistLanguageAsync(string language)
    {
        try
        {
            await _store.MutateAsync(root => root.Preferences.Language = language, "save_language");
            Status = LocalizationService.Tr("S.Status.LanguageChanged", "Язык интерфейса изменён.");
        }
        catch (Exception exception)
        {
            AppLog.Write("Language persistence failed.", exception);
            Status = LocalizationService.Tr("S.Status.LanguageSaveFailed", "Не удалось сохранить язык интерфейса.");
        }
    }
}
