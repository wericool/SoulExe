namespace SoulExe.ViewModels;

/// <summary>Formats download progress strings for setup and Models Hub.</summary>
public static class ModelDownloadStatusText
{
    public static string Progress(string filePath, string progressDisplay) =>
        $"Скачивание {filePath}: {progressDisplay}";

    public static string Pausing => "Ставлю загрузку на паузу: текущий фрагмент сохраняется…";
    public static string Cancelling => "Отменяю загрузку: частичный файл будет удалён…";
    public static string Cancelled => "Загрузка отменена. Частичный файл удалён из SoulExeData.";
}
