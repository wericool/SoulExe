using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class ModelsHubService : IDisposable
{
    private readonly JsonDataStore _store;
    private readonly DataPaths _paths;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public ModelsHubService(JsonDataStore store)
    {
        _store = store;
        _paths = store.Paths;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("SoulExe/0.3 (local Windows application)");
    }

    public async Task<IReadOnlyList<ModelHubSearchResult>> SearchAsync(string query, CancellationToken token = default)
    {
        var prepared = string.IsNullOrWhiteSpace(query) ? "gguf" : $"{query} gguf";
        var endpoint = $"https://huggingface.co/api/models?search={Uri.EscapeDataString(prepared)}&limit=30&sort=downloads&direction=-1";
        using var response = await _http.GetAsync(endpoint, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var raw = await JsonSerializer.DeserializeAsync<List<HuggingFaceModelDto>>(stream, _json, token) ?? [];
        return raw.Where(x => !string.IsNullOrWhiteSpace(x.Id) && !IsWhisperRepository(x.Id!))
            .Select(x => new ModelHubSearchResult(x.Id!, x.Author ?? "Unknown", x.Downloads, x.Likes, x.PipelineTag ?? "", x.LastModified))
            .ToList();
    }

    public async Task<ModelHubDetails> GetDetailsAsync(ModelHubSearchResult result, CancellationToken token = default)
    {
        var summary = "Описание репозитория загружается с Hugging Face.";
        try
        {
            var readmeUrl = $"https://huggingface.co/{result.RepositoryId}/raw/main/README.md";
            var readme = await _http.GetStringAsync(readmeUrl, token);
            summary = ExtractReadmeSummary(readme);
            if (string.IsNullOrWhiteSpace(summary)) summary = "Автор не добавил читаемое описание в README.";
        }
        catch { summary = "Описание README временно недоступно. Метаданные ниже получены из каталога Hugging Face."; }
        return new ModelHubDetails(result.RepositoryId, result.Author, result.Downloads, result.Likes, result.PipelineTag, result.LastModified, summary);
    }

    private static string ExtractReadmeSummary(string readme)
    {
        if (string.IsNullOrWhiteSpace(readme)) return "";
        var text = readme.Trim();
        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            var end = text.IndexOf("---", 3, StringComparison.Ordinal);
            if (end >= 0) text = text[(end + 3)..].Trim();
        }
        text = text.Replace("#", "").Replace("`", "").Replace("**", "");
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !x.TrimStart().StartsWith("!", StringComparison.Ordinal) && !x.TrimStart().StartsWith("[", StringComparison.Ordinal))
            .Select(x => x.Trim());
        var result = string.Join(" ", lines).Trim();
        return result.Length > 1400 ? result[..1400] + "…" : result;
    }

    public async Task<IReadOnlyList<ModelHubFile>> GetGgufFilesAsync(string repositoryId, CancellationToken token = default)
    {
        var endpoint = $"https://huggingface.co/api/models/{repositoryId}/tree/main?recursive=true&expand=false";
        using var response = await _http.GetAsync(endpoint, token);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var raw = await JsonSerializer.DeserializeAsync<List<HuggingFaceFileDto>>(stream, _json, token) ?? [];
        return raw.Where(x => x.Path?.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) == true)
            .Select(x => new ModelHubFile(x.Path!, x.Size ?? 0))
            .OrderBy(x => x.SizeBytes)
            .ToList();
    }

    public async Task<SoulModelInstallation> DownloadModelAsync(
        string repositoryId,
        ModelHubFile file,
        Action<ModelDownloadProgress>? progress = null,
        Action<string>? status = null,
        CancellationToken token = default)
    {
        if (IsWhisperRepository(repositoryId))
            throw new InvalidOperationException("Whisper — это модель распознавания речи, а не текстовая LLM для чата. Она исключена из текстовой версии SoulExe.");
        var safeRepo = string.Concat(repositoryId.Select(x => char.IsLetterOrDigit(x) || x is '-' or '_' or '.' ? x : '_'));
        var fileName = Path.GetFileName(file.Path);
        var directory = Path.Combine(_paths.ModelDirectory, "llm", safeRepo);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, fileName);
        var partial = destination + ".partial";
        await DownloadWithResumeAsync(repositoryId, file.Path, partial, file.SizeBytes, progress, status, token);
        File.Move(partial, destination, overwrite: true);

        var record = new SoulModelInstallation
        {
            Kind = "llm",
            Backend = "cpu",
            DisplayName = fileName,
            SourceUri = $"https://huggingface.co/{repositoryId}",
            LocalPath = destination,
            SizeBytes = new FileInfo(destination).Length,
            Metadata = new Dictionary<string, string>
            {
                ["repository_id"] = repositoryId,
                ["repository_file"] = file.Path
            }
        };
        await _store.MutateAsync(root =>
        {
            root.Models.RemoveAll(x => string.Equals(x.LocalPath, destination, StringComparison.OrdinalIgnoreCase));
            root.Models.Add(record);
        }, "download_model", token);
        return record;
    }

    public void DiscardPartialDownload(string repositoryId, ModelHubFile file)
    {
        var safeRepo = string.Concat(repositoryId.Select(x => char.IsLetterOrDigit(x) || x is '-' or '_' or '.' ? x : '_'));
        var partial = Path.Combine(_paths.ModelDirectory, "llm", safeRepo, Path.GetFileName(file.Path) + ".partial");
        if (File.Exists(partial)) File.Delete(partial);
    }

    public async Task RegisterExistingModelAsync(string path, CancellationToken token = default)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("GGUF-файл не найден.", path);
        if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Нужен файл формата GGUF.");
        var record = new SoulModelInstallation
        {
            Kind = "llm",
            Backend = "cpu",
            DisplayName = Path.GetFileName(path),
            LocalPath = path,
            SizeBytes = new FileInfo(path).Length,
            SourceUri = "local"
        };
        await _store.MutateAsync(root =>
        {
            root.Models.RemoveAll(x => string.Equals(x.LocalPath, path, StringComparison.OrdinalIgnoreCase));
            root.Models.Add(record);
        }, "register_model", token);
    }

    public Task<IReadOnlyList<SoulModelInstallation>> GetInstalledModelsAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulModelInstallation>)root.Models
            .Where(x => x.Kind == "llm" && File.Exists(x.LocalPath))
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList(), token);

    public async Task<IReadOnlyList<SoulModelInstallation>> RefreshInstalledModelsAsync(CancellationToken token = default)
    {
        Directory.CreateDirectory(_paths.ModelDirectory);
        var paths = Directory.EnumerateFiles(_paths.ModelDirectory, "*.gguf", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return await _store.MutateAsync(root =>
        {
            root.Models.RemoveAll(x => x.Kind == "llm" && !File.Exists(x.LocalPath));
            foreach (var path in paths)
            {
                if (root.Models.Any(x => x.Kind == "llm" && string.Equals(x.LocalPath, path, StringComparison.OrdinalIgnoreCase))) continue;
                root.Models.Add(new SoulModelInstallation
                {
                    Kind = "llm",
                    Backend = "cpu",
                    DisplayName = Path.GetFileName(path),
                    SourceUri = "local_scan",
                    LocalPath = path,
                    SizeBytes = new FileInfo(path).Length,
                    Metadata = new Dictionary<string, string> { ["discovered"] = "true" }
                });
            }
            return (IReadOnlyList<SoulModelInstallation>)root.Models
                .Where(x => x.Kind == "llm" && File.Exists(x.LocalPath))
                .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, "refresh_installed_models", token);
    }

    private async Task DownloadWithResumeAsync(
        string repositoryId,
        string relativePath,
        string partialPath,
        long expectedSize,
        Action<ModelDownloadProgress>? progress,
        Action<string>? status,
        CancellationToken token)
    {
        var initialSize = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (expectedSize > 0 && initialSize == expectedSize)
        {
            status?.Invoke("Найдена полностью загруженная временная копия. Завершаю сохранение модели…");
            progress?.Invoke(new ModelDownloadProgress(initialSize, expectedSize));
            return;
        }

        const int maxAttempts = 4;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await DownloadAttemptAsync(repositoryId, relativePath, partialPath, expectedSize, progress, status, token);
                return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientDownloadFailure(ex) && attempt < maxAttempts)
            {
                lastError = ex;
                var saved = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                var retryDelay = TimeSpan.FromSeconds(attempt * 2);
                status?.Invoke($"Соединение прервано. Сохранено {FormatBytes(saved)}. Повтор {attempt} из {maxAttempts - 1} через {retryDelay.TotalSeconds:0} сек…");
                await Task.Delay(retryDelay, token);
            }
            catch
            {
                throw;
            }
        }

        var remaining = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        throw new IOException($"Не удалось продолжить загрузку после нескольких попыток. Уже сохранено {FormatBytes(remaining)}; после восстановления интернета нажмите «Продолжить».", lastError);
    }

    private async Task DownloadAttemptAsync(
        string repositoryId,
        string relativePath,
        string partialPath,
        long expectedSize,
        Action<ModelDownloadProgress>? progress,
        Action<string>? status,
        CancellationToken token)
    {
        var already = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        var encodedPath = string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));
        var url = $"https://huggingface.co/{repositoryId}/resolve/main/{encodedPath}?download=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (already > 0) request.Headers.Range = new RangeHeaderValue(already, null);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (already > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            status?.Invoke("Сервер не подтвердил докачку по частям. Начинаю этот файл заново, старый фрагмент будет заменён.");
            already = 0;
            try { File.Delete(partialPath); } catch { }
        }
        response.EnsureSuccessStatusCode();

        var serverLength = response.Content.Headers.ContentLength ?? 0;
        var total = expectedSize > 0 ? expectedSize : already + serverLength;
        if (already > 0)
            status?.Invoke($"Продолжаю загрузку с {FormatBytes(already)} из {FormatBytes(total)}…");
        else
            status?.Invoke($"Скачивание файла: {FormatBytes(total)}…");
        progress?.Invoke(new ModelDownloadProgress(already, total));

        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
        var buffer = new byte[128 * 1024];
        var received = already;
        var lastReport = DateTimeOffset.MinValue;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            received += read;
            if ((DateTimeOffset.Now - lastReport).TotalMilliseconds >= 250)
            {
                progress?.Invoke(new ModelDownloadProgress(received, total));
                lastReport = DateTimeOffset.Now;
            }
        }
        await output.FlushAsync(token);
        progress?.Invoke(new ModelDownloadProgress(received, total));
        if (expectedSize > 0 && received != expectedSize)
            throw new InvalidDataException($"Размер загруженной модели не совпал с каталогом ({received} вместо {expectedSize} байт).");
    }

    private static bool IsWhisperRepository(string repositoryId) =>
        repositoryId.Contains("whisper", StringComparison.OrdinalIgnoreCase) ||
        repositoryId.Contains("speech-to-text", StringComparison.OrdinalIgnoreCase) ||
        repositoryId.Contains("speech_to_text", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransientDownloadFailure(Exception exception) =>
        exception is HttpRequestException or IOException or TaskCanceledException;

    private static string FormatBytes(long bytes) =>
        bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824d:F2} ГБ" :
        bytes >= 1_048_576 ? $"{bytes / 1_048_576d:F0} МБ" :
        $"{bytes / 1024d:F0} КБ";

    public void Dispose() => _http.Dispose();

    private sealed class HuggingFaceModelDto
    {
        public string? Id { get; set; }
        public string? Author { get; set; }
        public long Downloads { get; set; }
        public long Likes { get; set; }
        public string? PipelineTag { get; set; }
        public DateTimeOffset? LastModified { get; set; }
    }

    private sealed class HuggingFaceFileDto
    {
        public string? Path { get; set; }
        public long? Size { get; set; }
    }
}

public sealed record ModelHubSearchResult(string RepositoryId, string Author, long Downloads, long Likes, string PipelineTag, DateTimeOffset? LastModified);
public sealed record ModelHubDetails(string RepositoryId, string Author, long Downloads, long Likes, string PipelineTag, DateTimeOffset? LastModified, string Description)
{
    public string UpdatedText => LastModified is null ? "Дата обновления не указана" : $"Обновлено: {LastModified.Value.LocalDateTime:g}";
    public string PipelineText => string.IsNullOrWhiteSpace(PipelineTag) ? "Тип модели не указан" : $"Тип: {PipelineTag}";
}
public sealed record ModelHubFile(string Path, long SizeBytes)
{
    public string DisplaySize => SizeBytes >= 1_073_741_824 ? $"{SizeBytes / 1_073_741_824d:F1} ГБ" : $"{SizeBytes / 1_048_576d:F0} МБ";
}
public sealed record ModelDownloadProgress(long ReceivedBytes, long TotalBytes)
{
    public double Percent => TotalBytes <= 0 ? 0 : Math.Clamp(ReceivedBytes * 100d / TotalBytes, 0, 100);
    public string Display => TotalBytes <= 0 ? $"{ReceivedBytes / 1_048_576d:F0} МБ" : $"{ReceivedBytes / 1_048_576d:F0} / {TotalBytes / 1_048_576d:F0} МБ ({Percent:F0}%)";
}
