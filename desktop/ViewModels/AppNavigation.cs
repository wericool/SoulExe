namespace SoulExe.ViewModels;

/// <summary>Page route aliases and display titles for the main shell.</summary>
public static class AppNavigation
{
    public static string NormalizePage(string? page)
    {
        var value = page ?? "Home";
        // Memory and lore now live inside the selected character card; scenes live inside Chats.
        if (string.Equals(value, "Memory", StringComparison.OrdinalIgnoreCase)) return "Characters";
        if (string.Equals(value, "Scene", StringComparison.OrdinalIgnoreCase)) return "Chat";
        return value;
    }

    public static string Title(string page) => page switch
    {
        "Home" => "Библиотека",
        "Chat" => "Разговоры",
        "Scene" => "ГРУППОВОЙ РАЗГОВОР",
        "Characters" => "Карточка персонажа",
        "Gateway" => "ХАБ",
        "Models" => "Models Hub",
        "Memory" => "Память",
        "Mobile" => "Мобильный доступ",
        "Options" => "Параметры",
        "Setup" => "Быстрый старт",
        _ => "SoulExe"
    };

    public static string Subtitle(string page) => page switch
    {
        "Home" => "Персонажи и загруженные лорбуки",
        "Chat" => "Диалоги и история всех персонажей",
        "Scene" => "Групповой разговор двух персонажей",
        "Characters" => "Настройка выбранного персонажа из Библиотеки",
        "Gateway" => "Каталог готовых персонажей, лорбуков и сценариев",
        "Models" => "Установка и выбор локальных GGUF-моделей",
        "Memory" => "Soul Memory и summary",
        "Mobile" => "Доступ с телефона в той же Wi‑Fi сети",
        "Options" => "Параметры локальной LLM и оборудования",
        "Setup" => "Движок llama.cpp и первая модель",
        _ => "Локальный текстовый AI"
    };

    public static string NormalizeModelsHubTab(string? tab) =>
        tab is "Catalog" or "Recommendations" or "Installed" ? tab : "Recommendations";

    public static string NormalizeOptionsTab(string? tab) =>
        string.Equals(tab, "appearance", StringComparison.OrdinalIgnoreCase) ? "appearance"
        : string.Equals(tab, "mobile", StringComparison.OrdinalIgnoreCase) ? "mobile"
        : "llm";
}
