using System.IO;
using System.Net.Http;

namespace Echo.Services;

/// <summary>
/// Downloads and manages the Whisper GGML model files.
/// Models are stored in %LOCALAPPDATA%\Echo\models\.
/// </summary>
public sealed class ModelManager
{
    // Multilingual models (no .en suffix) cover 99 languages. The .en variants are English-only
    // and slightly more accurate on English, but they cannot transcribe any other language.
    // "large-v3" is multilingual and the most accurate open Whisper model.
    private static readonly Dictionary<string, ModelInfo> AvailableModels = new()
    {
        ["base.en"] = new ModelInfo(
            "ggml-base.en.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin",
            147_964_211),
        ["tiny.en"] = new ModelInfo(
            "ggml-tiny.en.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.en.bin",
            77_691_713),
        ["small.en"] = new ModelInfo(
            "ggml-small.en.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en.bin",
            487_601_967),
        ["base"] = new ModelInfo(
            "ggml-base.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin",
            147_964_211),
        ["tiny"] = new ModelInfo(
            "ggml-tiny.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
            77_691_713),
        ["small"] = new ModelInfo(
            "ggml-small.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin",
            487_601_967),
        ["medium"] = new ModelInfo(
            "ggml-medium.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin",
            1_533_219_771),
        ["large-v3"] = new ModelInfo(
            "ggml-large-v3.bin",
            "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3.bin",
            3_117_447_771)
    };

    private readonly string _modelsDirectory;

    public ModelManager()
    {
        _modelsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo", "models");
        Directory.CreateDirectory(_modelsDirectory);
    }

    /// <summary>
    /// Gets the full path to the model file.
    /// </summary>
    public string GetModelPath(string modelName = "base.en")
    {
        if (!AvailableModels.TryGetValue(modelName, out var info))
            throw new ArgumentException($"Unknown model: {modelName}");

        return Path.Combine(_modelsDirectory, info.FileName);
    }

    /// <summary>
    /// Checks if the model file exists and has the expected size.
    /// </summary>
    public bool IsModelDownloaded(string modelName = "base.en")
    {
        string path = GetModelPath(modelName);
        if (!File.Exists(path)) return false;

        var fileInfo = new FileInfo(path);
        var modelInfo = AvailableModels[modelName];

        // Allow 5% tolerance on file size
        return Math.Abs(fileInfo.Length - modelInfo.ExpectedSize) < modelInfo.ExpectedSize * 0.05;
    }

    /// <summary>
    /// Finds the best available downloaded model on disk, if any.
    /// Prefers multilingual large/small first (more useful for non-English), then .en variants.
    /// </summary>
    public string? GetFirstAvailableModel()
    {
        // Highest accuracy first. Multilingual large-v3 wins, then the smaller multilingual
        // tiers, then the .en variants as a fallback.
        string[] priority = ["large-v3", "medium", "small", "base", "tiny",
                             "small.en", "base.en", "tiny.en"];
        foreach (var name in priority)
        {
            if (IsModelDownloaded(name))
                return name;
        }
        return null;
    }

    /// <summary>
    /// True when the model key is the .en-only variant. Those models can only transcribe
    /// English; Whisper rejects any other language code at decode time.
    /// </summary>
    public static bool IsEnglishOnlyModel(string modelName)
        => modelName.EndsWith(".en", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads the specified model file with progress reporting.
    /// </summary>
    public async Task DownloadModelAsync(
        string modelName = "base.en",
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!AvailableModels.TryGetValue(modelName, out var info))
            throw new ArgumentException($"Unknown model: {modelName}");

        string targetPath = Path.Combine(_modelsDirectory, info.FileName);
        string tempPath = targetPath + ".downloading";

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);

            using var response = await httpClient.GetAsync(info.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? info.ExpectedSize;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                long lastReported = 0;
                int bytesRead;

                // Report at most once per MB. Reporting every 80 KB chunk queued ~1,800
                // dispatcher callbacks for a 148 MB model and made the UI crawl.
                const long ReportInterval = 1024 * 1024;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;

                    if (totalRead - lastReported >= ReportInterval)
                    {
                        lastReported = totalRead;
                        progress?.Report((totalRead, totalBytes));
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                progress?.Report((totalRead, totalBytes));
            }

            // Single-step replace: deleting first left no model at all if the move failed.
            File.Move(tempPath, targetPath, overwrite: true);
            Logger.Info($"Model '{modelName}' downloaded to {targetPath}");
        }
        catch
        {
            // Never leave a half-written .downloading file behind ? a retry would otherwise
            // fail or, worse, a stale partial could be mistaken for a real model.
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not delete partial download '{tempPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Returns all available models with their download status.
    /// </summary>
    public List<(string Name, string FileName, long SizeMB, bool IsDownloaded)> GetModelsStatus()
    {
        return AvailableModels.Select(kvp => (
            kvp.Key,
            kvp.Value.FileName,
            kvp.Value.ExpectedSize / (1024 * 1024),
            IsModelDownloaded(kvp.Key)
        )).ToList();
    }

    /// <summary>
    /// All known model keys (e.g. "large-v3", "medium", "base.en"). The Settings UI uses
    /// this to render a list of what is on disk without knowing the internal dictionary.
    /// </summary>
    public IReadOnlyCollection<string> AvailableModelNames => AvailableModels.Keys;

    private sealed record ModelInfo(string FileName, string DownloadUrl, long ExpectedSize);
}
