using System.IO;
using Whisper.net;

namespace Echo.Services;

/// <summary>
/// Wraps Whisper.net for on-device speech-to-text transcription.
/// Pre-initializes a multi-threaded Whisper processor for sub-second latency.
/// </summary>
public sealed class TranscriptionService : IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _isLoaded;
    private string? _loadedModelPath;

    // null = send no prompt at all. Whisper treats the prompt as text that preceded the
    // audio, so a fabricated instruction sentence made the decoder continue in that
    // register and invent whole sentences on quiet input.
    private string? _prompt;
    private int _threadCount = 4;

    // Guards factory/processor lifetime. A SemaphoreSlim (not lock) so transcription can
    // hold it across awaits without blocking the UI thread inside a monitor.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public bool IsModelLoaded => _isLoaded;

    /// <summary>
    /// Path of the model currently held in memory, or null if none.
    /// </summary>
    public string? LoadedModelPath => _loadedModelPath;

    /// <summary>
    /// Loads the Whisper model from disk and creates the processor.
    /// Loading a different path swaps the model; reloading the same path is a no-op.
    /// </summary>
    public void LoadModel(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("Model path is empty.", nameof(modelPath));

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model not found at: {modelPath}");

        string fullPath = Path.GetFullPath(modelPath);

        _gate.Wait();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TranscriptionService));

            // Previously this returned early whenever *any* model was loaded, so picking a
            // new model in Settings silently kept using the old one. Only skip real re-loads.
            if (_isLoaded &&
                string.Equals(_loadedModelPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"Whisper model already loaded from {fullPath}; skipping reload.");
                return;
            }

            if (_isLoaded)
                Logger.Info($"Swapping Whisper model: {_loadedModelPath} -> {fullPath}");

            ReleaseUnsafe();

            Logger.Info($"Loading Whisper model into memory from: {fullPath}");
            _factory = WhisperFactory.FromPath(fullPath);

            // Configure multi-threaded processing for fast inference on CPU.
            // whisper.cpp scales with PHYSICAL cores. ProcessorCount reports logical CPUs,
            // and the extra SMT siblings contend for the same FMA units, so running 16
            // threads on an 8-core part is measurably SLOWER than running 8. Past ~8 threads
            // memory bandwidth binds anyway.
            int logical = Environment.ProcessorCount;
            _threadCount = Math.Clamp(logical >= 8 ? logical / 2 : logical, 1, 8);
            Logger.Info($"Configuring Whisper inference with {_threadCount} CPU threads ({logical} logical CPUs detected).");

            BuildProcessorUnsafe();

            _loadedModelPath = fullPath;
            _isLoaded = true;
            Logger.Info("Whisper model loaded and processor ready.");
        }
        catch
        {
            ReleaseUnsafe();
            _loadedModelPath = null;
            _isLoaded = false;
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sets the prompt used to bias recognition (custom vocabulary).
    /// Rebuilds the processor only when the prompt actually changes.
    /// </summary>
    public void SetPrompt(string? prompt)
    {
        string? effective = string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim();

        _gate.Wait();
        try
        {
            if (_disposed || effective == _prompt) return;

            _prompt = effective;

            if (_isLoaded && _factory != null)
            {
                BuildProcessorUnsafe();
                Logger.Info($"Whisper biasing prompt updated: \"{effective ?? "(none)"}\"");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to apply new Whisper prompt; keeping previous processor", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Transcribes float[] audio samples (16kHz, mono, normalized to [-1, 1]).
    /// </summary>
    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (samples.Length == 0)
            return string.Empty;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_isLoaded || _processor == null)
                throw new InvalidOperationException("Model not loaded. Call LoadModel() first.");

            var processor = _processor;

            // Offload to a worker thread: Whisper inference is a blocking native call, so
            // running it on the dispatcher would freeze the UI for the whole utterance.
            return await Task.Run(async () =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var resultText = new System.Text.StringBuilder();

                // Feed the samples straight in. The previous path re-encoded them into an
                // in-memory 16-bit WAV that Whisper.net then decoded back to float.
                await foreach (var segment in processor.ProcessAsync(samples).ConfigureAwait(false))
                {
                    resultText.Append(segment.Text);
                }

                Logger.Info($"Whisper inference took {stopwatch.ElapsedMilliseconds} ms for {samples.Length / 16000.0:F2}s of audio on {_threadCount} threads.");
                return resultText.ToString().Trim();
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void BuildProcessorUnsafe()
    {
        _processor?.Dispose();
        _processor = null;

        // Pick language code from the loaded model. .en models must use "en" ? Whisper rejects
        // any other value at decode time. Multilingual models use "auto" so Whisper detects
        // the spoken language instead of forcing English.
        string language = ResolveLanguageForModel(_loadedModelPath);

        var builder = _factory!.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(_threadCount)
            // Whisper's default is temperature fallback: if a segment trips the entropy or
            // log-prob threshold it re-runs the whole decode at 0.2, 0.4, ... up to six
            // passes for one utterance. That was most of the multi-second wait.
            .WithTemperature(0f)
            .WithTemperatureInc(0f)
            // Do not feed one segment's text back in as context for the next. On a quiet
            // mic that feedback is what turns a single misheard word into a fluent
            // invented sentence.
            .WithNoContext();

        // Only pass a prompt when there is real vocabulary to bias toward. An empty or
        // filler prompt costs accuracy for nothing.
        if (!string.IsNullOrEmpty(_prompt))
            builder = builder.WithPrompt(_prompt);

        // best_of defaults to 5 ? five candidate decodes per segment, four thrown away.
        // WithGreedySamplingStrategy() is typed as the interface, which does not expose
        // WithBestOf, so the concrete builder has to be recovered by cast.
        var greedy = (GreedySamplingStrategyBuilder)builder.WithGreedySamplingStrategy();

        _processor = greedy
            .WithBestOf(1)
            .ParentBuilder
            .Build();
    }

    /// <summary>
    /// Map the model filename back to a Whisper language code. .en variants are locked to
    /// English; everything else (multilingual ggml-*.bin) gets "auto" so the decoder
    /// detects language per segment.
    /// </summary>
    private static string ResolveLanguageForModel(string? modelPath)
    {
        if (string.IsNullOrEmpty(modelPath)) return "en";
        string name = Path.GetFileNameWithoutExtension(modelPath);
        // Strip leading "ggml-" so "ggml-base.en.bin" ? "base.en".
        if (name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase))
            name = name[5..];
        return name.EndsWith(".en", StringComparison.OrdinalIgnoreCase) ? "en" : "auto";
    }

    private void ReleaseUnsafe()
    {
        try { _processor?.Dispose(); }
        catch (Exception ex) { Logger.Warn($"Error disposing Whisper processor: {ex.Message}"); }
        _processor = null;

        try { _factory?.Dispose(); }
        catch (Exception ex) { Logger.Warn($"Error disposing Whisper factory: {ex.Message}"); }
        _factory = null;
    }

    /// <summary>
    /// Creates a valid WAV file MemoryStream from float[] audio samples.
    /// Kept for callers that need a real WAV container; the transcription path feeds
    /// Whisper the float[] directly.
    /// </summary>
    public static MemoryStream CreateWavStream(float[] samples, int sampleRate)
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        int channels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = samples.Length * blockAlign;

        // RIFF header
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                  // chunk size
        writer.Write((short)1);            // PCM format
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)bitsPerSample);

        // data chunk
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);

        // Convert float samples to 16-bit PCM
        foreach (float sample in samples)
        {
            short pcmSample = (short)(Math.Clamp(sample, -1.0f, 1.0f) * 32767);
            writer.Write(pcmSample);
        }

        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        // Dispose is reachable twice (explicit exit + Application.OnExit), so it must be idempotent.
        if (_disposed) return;

        try
        {
            _gate.Wait();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (_disposed)
        {
            _gate.Release();
            return;
        }

        try
        {
            _disposed = true;
            ReleaseUnsafe();
            _isLoaded = false;
            _loadedModelPath = null;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
