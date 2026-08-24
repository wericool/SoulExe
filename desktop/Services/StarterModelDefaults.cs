namespace SoulExe.Services;

/// <summary>Built-in compact starter model used from Models Hub / setup.</summary>
public static class StarterModelDefaults
{
    public const string HuggingFaceRepository = "ggml-org/Qwen3.5-0.8B-GGUF";
    public const string SelectedMessage = "Выбрана компактная стартовая модель. При первом запуске llama.cpp скачает её автоматически.";
}
