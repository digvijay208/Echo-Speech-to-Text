namespace Echo.Services.Stt;

/// <summary>
/// Adapter that lets the existing Whisper.net-based
/// <see cref="TranscriptionService"/> participate in the ISttProvider
/// pipeline. Just forwards Load/Transcribe; lifecycle of the underlying
/// service is managed by App.xaml.cs.
/// </summary>
public sealed class WhisperSttProvider : ISttProvider
{
    private readonly TranscriptionService _inner;
    private bool _disposed;

    public WhisperSttProvider(TranscriptionService inner)
    {
        _inner = inner;
    }

    public string Name
    {
        get
        {
            var path = _inner.LoadedModelPath;
            if (string.IsNullOrEmpty(path)) return "Whisper (no model loaded)";
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("ggml-", System.StringComparison.OrdinalIgnoreCase))
                name = name[5..];
            return $"Whisper {name}";
        }
    }

    public string Tier => "local";
    public bool IsLoaded => _inner.IsModelLoaded;

    public void Load(string modelPathOrKey) => _inner.LoadModel(modelPathOrKey);

    public Task<string> TranscribeAsync(float[] samples16k, CancellationToken cancellationToken = default)
        => _inner.TranscribeAsync(samples16k);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // _inner is owned by App; do not dispose here.
    }
}
