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
    // "base" and "tiny" are the lightest multilingual options we ship.
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
            77_691_713)
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
    /// Prefers multilingual base/tiny first (more useful for non-English), then .en variants.
    /// </summary>
    public string? GetFirstAvailableModel()
    {
        // Highest accuracy first. Multilingual base wins, then tiny, then the .en variants.
        string[] priority = ["base", "tiny",
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
        var list = AvailableModels.Select(kvp => (
            kvp.Key,
            kvp.Value.FileName,
            kvp.Value.ExpectedSize / (1024 * 1024),
            IsModelDownloaded(kvp.Key)
        )).ToList();

        // Surface the Parakeet bundle as one virtual entry so the Settings
        // UI lists it next to the Whisper .bin files.
        list.Add(("parakeet-tdt-0.6b-v3-int8",
                  "encoder+decoder+joiner+tokens",
                  ParakeetBundleSize / (1024 * 1024),
                  IsParakeetDownloaded()));
        return list;
    }

    /// <summary>
    /// All known model keys (e.g. "base", "tiny", "base.en"). The Settings UI uses
    /// this to render a list of what is on disk without knowing the internal dictionary.
    /// </summary>
    public IReadOnlyCollection<string> AvailableModelNames => AvailableModels.Keys;

    // ========================================================================
    // Parakeet TDT 0.6B V3 (INT8) bundle
    //
    // The Parakeet model is a directory of four files (encoder, decoder,
    // joiner, tokens). We download them one at a time with a shared
    // progress reporter so the UI sees a single combined progress bar.
    // ========================================================================

    // Real sizes (Content-Length) from the HF repo; the old guessed values
    // made the size-check re-download files and the progress bar report >100%.
    private const long ParakeetBundleSize = 670_000_000;

    private sealed record ParakeetFile(string FileName, string Url, long ExpectedSize);

    private static readonly Dictionary<string, ParakeetFile> ParakeetFiles = new()
    {
        ["encoder.int8.onnx"] = new(
            "encoder.int8.onnx",
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/encoder.int8.onnx",
            652_184_281),
        ["decoder.int8.onnx"] = new(
            "decoder.int8.onnx",
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/decoder.int8.onnx",
            11_845_275),
        ["joiner.int8.onnx"] = new(
            "joiner.int8.onnx",
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/joiner.int8.onnx",
            6_355_277),
        ["tokens.txt"] = new(
            "tokens.txt",
            "https://huggingface.co/csukuangfj/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8/resolve/main/tokens.txt",
            93_939)
    };

    /// <summary>Directory containing the Parakeet TDT V3 files, e.g. ".../models/parakeet-tdt-0.6b-v3-int8".</summary>
    public string GetParakeetModelDirectory()
        => Path.Combine(_modelsDirectory, "parakeet-tdt-0.6b-v3-int8");

    public bool IsParakeetDownloaded()
    {
        string dir = GetParakeetModelDirectory();
        if (!Directory.Exists(dir)) return false;
        return ParakeetFiles.Keys.All(name => File.Exists(Path.Combine(dir, name)));
    }

    /// <summary>
    /// Downloads the four-file Parakeet bundle sequentially. Reports
    /// cumulative progress across the whole bundle (not per file) so the
    /// UI sees 0..100% once over the full ~478 MB.
    /// </summary>
    public async Task DownloadParakeetAsync(
        IProgress<(long downloaded, long total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string dir = GetParakeetModelDirectory();
        Directory.CreateDirectory(dir);

        long alreadyDownloaded = 0;
        foreach (var name in ParakeetFiles.Keys)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path))
                alreadyDownloaded += new FileInfo(path).Length;
        }
        long totalSize = ParakeetFiles.Values.Sum(f => f.ExpectedSize);

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(30);

        foreach (var (fileName, file) in ParakeetFiles)
        {
            string targetPath = Path.Combine(dir, fileName);
            if (File.Exists(targetPath))
            {
                long existing = new FileInfo(targetPath).Length;
                if (Math.Abs(existing - file.ExpectedSize) < file.ExpectedSize * 0.05)
                {
                    Logger.Info($"Parakeet '{fileName}' already present ({existing} bytes); skipping.");
                    continue;
                }
            }

            string tempPath = targetPath + ".downloading";
            try
            {
                Logger.Info($"Downloading Parakeet file '{fileName}'...");
                using var response = await httpClient.GetAsync(file.Url,
                    HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                long fileTotal = response.Content.Headers.ContentLength ?? file.ExpectedSize;
                long fileAlreadyDownloaded = File.Exists(targetPath) ? new FileInfo(targetPath).Length : 0;

                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, bufferSize: 81920);

                var buffer = new byte[81920];
                long fileRead = 0;
                long lastReported = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    fileRead += bytesRead;

                    if (fileRead - lastReported >= 1024 * 1024)
                    {
                        lastReported = fileRead;
                        progress?.Report((alreadyDownloaded + fileRead, totalSize));
                    }
                }

                await fileStream.FlushAsync(cancellationToken);
                File.Move(tempPath, targetPath, overwrite: true);
                alreadyDownloaded += fileRead;
                progress?.Report((alreadyDownloaded, totalSize));
                Logger.Info($"Parakeet '{fileName}' downloaded ({fileRead} bytes).");
            }
            catch
            {
                TryDeleteTemp(tempPath);
                throw;
            }
        }

        Logger.Info("Parakeet TDT 0.6B V3 bundle complete.");
    }

    private sealed record ModelInfo(string FileName, string DownloadUrl, long ExpectedSize);
}
