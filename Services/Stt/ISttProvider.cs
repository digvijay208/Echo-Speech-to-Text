namespace Echo.Services.Stt;

/// <summary>
/// One speech-to-text engine. Implementations: <c>WhisperProvider</c>
/// (wraps <c>TranscriptionService</c>), <c>ParakeetProvider</c>
/// (sherpa-onnx), <c>GroqProvider</c> (HTTP).
///
/// All providers consume 16 kHz mono float32 in [-1, 1] and return the raw
/// transcript. Dictionary biasing and cleanup happen one layer up in
/// <c>DictationService</c>.
/// </summary>
public interface ISttProvider : IDisposable
{
    /// <summary>Human-readable engine name, e.g. "Whisper base.en" or "Groq Whisper Large V3 Turbo".</summary>
    string Name { get; }

    /// <summary>Where this provider is the right pick (for diagnostics, surfaced in Settings).</summary>
    string Tier { get; }

    bool IsLoaded { get; }

    /// <summary>
    /// Prepare the engine. <paramref name="modelPathOrKey"/> is the Whisper .bin
    /// path, the Parakeet model directory, or the Groq API key, depending on
    /// the provider.
    /// </summary>
    void Load(string modelPathOrKey);

    /// <summary>Transcribe the given audio. Implementations must be safe to call concurrently across providers, not across calls to the same provider.</summary>
    Task<string> TranscribeAsync(float[] samples16k, CancellationToken cancellationToken = default);
}
