using SoulExe.Services;

namespace SoulExe.ViewModels;

/// <summary>Page route aliases and display titles for the main shell.
/// Titles are localized through LocalizationService; the inline switches are
/// the Russian fallbacks so the shell stays correct before/without dictionaries.</summary>
public static class AppNavigation
{
    public static string NormalizePage(string? page)
    {
        var value = page ?? "Home";
        // Memory and lore now live inside the selected character card; scenes live inside Chats.
        if (string.Equals(value, "Memory", StringComparison.OrdinalIgnoreCase)) return "Characters";
        if (string.Equals(value, "Scene", StringComparison.OrdinalIgnoreCase)) return "Chat";
        // Mobile access, Models Hub and Quick Start live inside Settings as tabs.
        if (string.Equals(value, "Mobile", StringComparison.OrdinalIgnoreCase)) return "Options";
        if (string.Equals(value, "Models", StringComparison.OrdinalIgnoreCase)) return "Options";
        if (string.Equals(value, "Setup", StringComparison.OrdinalIgnoreCase)) return "Options";
        return value;
    }

    public static string? OptionsTabForRoute(string? page) =>
        string.Equals(page, "Mobile", StringComparison.OrdinalIgnoreCase) ? "mobile"
        : string.Equals(page, "Models", StringComparison.OrdinalIgnoreCase) ? "models"
        : string.Equals(page, "Setup", StringComparison.OrdinalIgnoreCase) ? "setup"
        : null;

    public static string Title(string page) => Localized(page, "title", FallbackTitle(page));

    public static string Subtitle(string page) => Localized(page, "subtitle", FallbackSubtitle(page));

    private static string Localized(string page, string kind, string fallback) =>
        LocalizationService.Tr($"page.{page.ToLowerInvariant()}.{kind}", fallback);

    private static string FallbackTitle(string page) => page switch
    {
        "Home" => "Библиотека",
        "Chat" => "Разговоры",
        "Scene" => "Групповой разговор",
        "Characters" => "Карточка персонажа",
        "Gateway" => "Хаб",
        "Models" => "Models Hub",
        "Memory" => "Память",
        "Mobile" => "Мобильный доступ",
        "Options" => "Настройки",
        "Setup" => "Быстрый старт",
        _ => LocalizationService.Tr("page.fallback.title", "SoulExe")
    };

    private static string FallbackSubtitle(string page) => page switch
    {
        "Home" => "Персонажи и загруженные лорбуки",
        "Chat" => "Диалоги и история всех персонажей",
        "Scene" => "Групповой разговор двух персонажей",
        "Characters" => "Настройка выбранного персонажа из Библиотеки",
        "Gateway" => "Каталог готовых персонажей, лорбуков и сценариев",
        "Models" => "Установка и выбор локальных GGUF-моделей",
        "Memory" => "Soul Memory и summary",
        "Mobile" => "Доступ с телефона в той же Wi-Fi сети",
        "Options" => "Runtime, оформление интерфейса и доступ с мобильных устройств",
        "Setup" => "Движок llama.cpp и первая модель",
        _ => LocalizationService.Tr("page.fallback.subtitle", "Локальный текстовый AI")
    };

    public static string NormalizeModelsHubTab(string? tab) =>
        tab is "Catalog" or "Recommendations" or "Installed" ? tab : "Recommendations";

    public static string NormalizeOptionsTab(string? tab) =>
        string.Equals(tab, "appearance", StringComparison.OrdinalIgnoreCase) ? "appearance"
        : string.Equals(tab, "mobile", StringComparison.OrdinalIgnoreCase) ? "mobile"
        : string.Equals(tab, "models", StringComparison.OrdinalIgnoreCase) ? "models"
        : string.Equals(tab, "setup", StringComparison.OrdinalIgnoreCase) ? "setup"
        : "llm";
}
