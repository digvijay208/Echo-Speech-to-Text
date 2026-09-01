using System.IO;
using SherpaOnnx;

namespace Echo.Services.Stt;

/// <summary>
/// NVIDIA Parakeet TDT 0.6B V3 via sherpa-onnx. INT8 build so the total
/// footprint is ~478 MB on disk and a similar working-set in RAM. Faster
/// than Whisper Small on English with similar or better accuracy.
///
/// Model directory is expected to contain:
///   encoder.int8.onnx, decoder.int8.onnx, joiner.int8.onnx, tokens.txt
/// as published under csukuangfj/sherpa-onnx-models on Hugging Face.
/// </summary>
public sealed class ParakeetProvider : ISttProvider
{
    private OnlineRecognizer? _recognizer;
    private string? _modelDir;
    private bool _disposed;

    public string Name => "Parakeet TDT 0.6B V3 (INT8)";
    public string Tier => "local";
    public bool IsLoaded => _recognizer != null;

    public void Load(string modelPathOrKey)
    {
        if (string.IsNullOrWhiteSpace(modelPathOrKey))
            throw new ArgumentException("Parakeet model path is empty.", nameof(modelPathOrKey));

        if (!Directory.Exists(modelPathOrKey))
            throw new DirectoryNotFoundException(
                $"Parakeet model directory not found: {modelPathOrKey}");

        string encoder = Path.Combine(modelPathOrKey, "encoder.int8.onnx");
        string decoder = Path.Combine(modelPathOrKey, "decoder.int8.onnx");
        string joiner  = Path.Combine(modelPathOrKey, "joiner.int8.onnx");
        string tokens  = Path.Combine(modelPathOrKey, "tokens.txt");

        foreach (var (label, path) in new[] { ("encoder", encoder), ("decoder", decoder), ("joiner", joiner), ("tokens", tokens) })
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(
                    $"Parakeet model is missing the {label} file. Expected at: {path}");
        }

        try
        {
            // OnlineRecognizerConfig is a struct with public fields, not a
            // class with settable properties. The endpoint-detection
            // fields live directly on it (no nested EndpointConfig type).
            int logical = Environment.ProcessorCount;
            int threads = Math.Clamp(logical >= 8 ? logical / 2 : logical, 1, 8);

            var modelConfig = new OnlineModelConfig();
            modelConfig.Transducer.Encoder = encoder;
            modelConfig.Transducer.Decoder = decoder;
            modelConfig.Transducer.Joiner  = joiner;
            modelConfig.Tokens = tokens;
            modelConfig.NumThreads = threads;
            modelConfig.Provider = "cpu";
            modelConfig.Debug = 0;

            var config = new OnlineRecognizerConfig
            {
                FeatConfig = new FeatureConfig { SampleRate = 16_000, FeatureDim = 80 },
                ModelConfig = modelConfig,
                DecodingMethod = "greedy_search",
                // Disable the recognizer's own endpoint detection: we
                // already segment with Silero VAD upstream, and a brief
                // pause inside a long utterance should not end the
                // decoding pass.
                EnableEndpoint = 0
            };

            _recognizer = new OnlineRecognizer(config);
            _modelDir = modelPathOrKey;
            Logger.Info($"ParakeetProvider loaded from {modelPathOrKey} ({threads} threads).");
        }
        catch
        {
            try { _recognizer?.Dispose(); } catch { /* ignore */ }
            _recognizer = null;
            _modelDir = null;
            throw;
        }
    }

    public Task<string> TranscribeAsync(float[] samples16k, CancellationToken cancellationToken = default)
    {
        if (_recognizer == null)
            throw new InvalidOperationException("Parakeet model not loaded. Call Load() first.");
        if (samples16k.Length == 0) return Task.FromResult(string.Empty);

        // sherpa-onnx is blocking; offload to a worker thread so callers can
        // await without parking the UI dispatcher.
        return Task.Run(() =>
        {
            var stream = _recognizer.CreateStream();
            try
            {
                stream.AcceptWaveform(16_000, samples16k);
                stream.InputFinished();

                while (_recognizer.IsReady(stream))
                    _recognizer.Decode(stream);

                var text = _recognizer.GetResult(stream).Text;
                return text?.Trim() ?? string.Empty;
            }
            finally
            {
                stream.Dispose();
            }
        }, cancellationToken);
    }

    public string GetModelDirectory() => _modelDir ?? string.Empty;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _recognizer?.Dispose(); }
        catch (Exception ex) { Logger.Warn($"Error disposing Parakeet recognizer: {ex.Message}"); }
        _recognizer = null;
    }
}
