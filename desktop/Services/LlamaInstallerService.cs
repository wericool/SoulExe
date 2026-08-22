using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SoulTextWpf.Services;

public sealed record LlamaBackendOption(string Id, string DisplayName, string Description, string ExampleHardware, string PackageUrl, string? RuntimeUrl = null)
{
    /// <summary>UI flag refreshed from disk; not part of equality.</summary>
    public bool IsInstalled { get; set; }
}

public sealed class LlamaInstallerService : IDisposable
{
    // Official ggml-org/llama.cpp release assets. A backend has a private folder so CPU and GPU engines never overwrite one another.
    private const string Release = "b10472";
    private static readonly IReadOnlyList<LlamaBackendOption> BackendOptions =
    [
        new("cpu", "CPU", "Любой ПК без требований к видеокарте. Медленнее GPU, но самый надёжный старт для новичка.", "Пример: офисный ПК или ноутбук без дискретной видеокарты.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-cpu-x64.zip"),
        new("cuda12", "CUDA 12 (NVIDIA)", "Лучший выбор для большинства NVIDIA GeForce/RTX. Runtime DLL ставятся вместе с движком.", "Примеры: GTX 1660, RTX 2060, RTX 3060, RTX 4060, RTX 4070.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-cuda-12.4-x64.zip", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/cudart-llama-bin-win-cuda-12.4-x64.zip"),
        new("cuda13", "CUDA 13 (NVIDIA)", "Для NVIDIA с очень свежим драйвером. Если CUDA 13 не запускается — возьмите CUDA 12.", "Примеры: RTX 5060, RTX 5070, RTX 5080, RTX 5090 с актуальным драйвером.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-cuda-13.3-x64.zip", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/cudart-llama-bin-win-cuda-13.3-x64.zip"),
        new("vulkan", "Vulkan", "Универсальное GPU-ускорение (NVIDIA/AMD/Intel), когда CUDA недоступна или не ставится.", "Примеры: AMD RX 6600/7600/7800 XT, NVIDIA GTX/RTX, Intel Arc A770/B580.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-vulkan-x64.zip"),
        new("rocm", "ROCm / HIP (AMD)", "Для совместимых AMD Radeon с ROCm runtime. Не для NVIDIA и не для Intel.", "Примеры: AMD Radeon RX 7900 XT/XTX и RX 9070 XT при совместимом ROCm.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-rocm-7.14-x64.zip"),
        new("sycl", "SYCL (Intel)", "Для Intel Arc и части Intel iGPU с oneAPI/SYCL. При проблемах — Vulkan или CPU.", "Примеры: Intel Arc A580, A750, A770, B580; часть поддерживаемых Intel iGPU.", $"https://github.com/ggml-org/llama.cpp/releases/download/{Release}/llama-{Release}-bin-win-sycl-x64.zip")
    ];

    private readonly string _engineRoot;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public LlamaInstallerService(DataPaths? paths = null)
    {
        var dataPaths = paths ?? AppServices.Paths;
        _engineRoot = Path.Combine(dataPaths.EngineDirectory, "llama-cpp");
    }

    public IReadOnlyList<LlamaBackendOption> AvailableBackends => BackendOptions;
    public string ManagedServerPath => GetManagedServerPath("cpu");
    public string EngineDirectory => _engineRoot;
    public bool IsInstalled => IsBackendInstalled("cpu");

    public LlamaBackendOption GetBackend(string? backendId) => BackendOptions.FirstOrDefault(x => string.Equals(x.Id, backendId, StringComparison.OrdinalIgnoreCase)) ?? BackendOptions[0];
    public string GetManagedServerPath(string? backendId) => Path.Combine(_engineRoot, GetBackend(backendId).Id, "llama-server.exe");
    public bool IsBackendInstalled(string? backendId) => File.Exists(GetManagedServerPath(backendId));

    public Task<string> InstallCpuEngineAsync(Action<string>? progress = null, CancellationToken cancellationToken = default) =>
        InstallEngineAsync("cpu", progress, cancellationToken);

    public async Task<string> InstallEngineAsync(string? backendId, Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var backend = GetBackend(backendId);
        var destinationDirectory = Path.Combine(_engineRoot, backend.Id);
        var destinationServer = GetManagedServerPath(backend.Id);
        if (IsBackendInstalled(backend.Id))
        {
            progress?.Invoke($"{backend.DisplayName}: llama.cpp уже установлен.");
            return destinationServer;
        }

        // Все промежуточные архивы и распаковка остаются в переносимой папке рядом с SoulExe.exe.
        var temporaryRoot = Path.Combine(_engineRoot, "_staging", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            progress?.Invoke($"Скачиваю движок llama.cpp: {backend.DisplayName}…");
            var primaryExtract = await DownloadAndExtractAsync(backend.PackageUrl, temporaryRoot, "engine", progress, cancellationToken);
            if (!string.IsNullOrWhiteSpace(backend.RuntimeUrl))
            {
                progress?.Invoke($"Скачиваю runtime DLL для {backend.DisplayName}…");
                await DownloadAndExtractAsync(backend.RuntimeUrl, temporaryRoot, "runtime", progress, cancellationToken);
            }

            var server = Directory.EnumerateFiles(primaryExtract, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (server is null) throw new InvalidOperationException("В официальном архиве llama.cpp не найден llama-server.exe.");
            var stage = Path.Combine(temporaryRoot, "stage");
            Directory.CreateDirectory(stage);
            CopyFilesFlattened(Path.GetDirectoryName(server)!, stage);
            var runtimeExtract = Path.Combine(temporaryRoot, "runtime");
            if (Directory.Exists(runtimeExtract)) CopyFilesFlattened(runtimeExtract, stage);
            if (!File.Exists(Path.Combine(stage, "llama-server.exe"))) throw new InvalidOperationException("Движок llama.cpp распакован неполностью.");

            if (Directory.Exists(destinationDirectory)) Directory.Delete(destinationDirectory, recursive: true);
            Directory.CreateDirectory(destinationDirectory);
            progress?.Invoke($"Копирую {backend.DisplayName} в SoulExeData…");
            CopyFilesFlattened(stage, destinationDirectory);
            if (!File.Exists(destinationServer)) throw new InvalidOperationException("Не удалось скопировать llama-server.exe в SoulExeData.");
            progress?.Invoke($"{backend.DisplayName}: llama.cpp установлен в SoulExeData.");
            return destinationServer;
        }
        finally { try { Directory.Delete(temporaryRoot, recursive: true); } catch { } }
    }

    private async Task<string> DownloadAndExtractAsync(string url, string temporaryRoot, string folderName, Action<string>? progress, CancellationToken token)
    {
        var archivePath = Path.Combine(temporaryRoot, $"{folderName}.zip");
        var extractPath = Path.Combine(temporaryRoot, folderName);
        await DownloadAsync(url, archivePath, progress, token);
        ZipFile.ExtractToDirectory(archivePath, extractPath);
        return extractPath;
    }

    private static void CopyFilesFlattened(string sourceDirectory, string destinationDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
    }

    private async Task DownloadAsync(string url, string destination, Action<string>? progress, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(destination);
        var buffer = new byte[128 * 1024]; long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if (total is > 0) progress?.Invoke($"Скачиваю llama.cpp: {received * 100 / total.Value}%");
        }
    }

    public void Dispose() => _http.Dispose();
}
