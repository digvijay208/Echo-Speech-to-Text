namespace Echo.Services.Stt;

/// <summary>
/// Picks the right <see cref="ISttProvider"/> for a given transcription
/// request, with graceful fallback when the chosen engine is not
/// available. Owns all three providers; callers (DictationService) only
/// ever see one ISttProvider interface.
///
/// Fallback order (when cloud chosen): Groq -> Parakeet -> Whisper.
/// Fallback order (when local chosen): Parakeet -> Whisper.
/// </summary>
public sealed class SttRouter : ISttProvider
{
    private readonly ISttProvider _whisper;
    private readonly ParakeetProvider _parakeet;
    private readonly GroqProvider _groq;
    private readonly Func<EchoSettings.SttEngine> _activeEngine;
    private readonly Func<bool> _allowCloud;

    public SttRouter(
        ISttProvider whisper,
        ParakeetProvider parakeet,
        GroqProvider groq,
        Func<EchoSettings.SttEngine> activeEngine,
        Func<bool> allowCloud)
    {
        _whisper = whisper;
        _parakeet = parakeet;
        _groq = groq;
        _activeEngine = activeEngine;
        _allowCloud = allowCloud;
    }

    public string Name
    {
        get
        {
            var pick = Resolve(out _, out _);
            return pick.Name;
        }
    }

    public string Tier
    {
        get
        {
            var pick = Resolve(out _, out _);
            return pick.Tier;
        }
    }

    public bool IsLoaded
    {
        get
        {
            var pick = Resolve(out _, out _);
            return pick.IsLoaded;
        }
    }

    public void Load(string modelPathOrKey)
    {
        // The router's own "load" is only the Whisper path; Parakeet and
        // Groq each have their own Load() and are called directly from
        // App.xaml.cs when their respective settings change.
        _whisper.Load(modelPathOrKey);
    }

    public async Task<string> TranscribeAsync(float[] samples16k, CancellationToken cancellationToken = default)
    {
        if (samples16k.Length == 0) return string.Empty;

        var pick = Resolve(out var preferred, out var reason);
        if (pick == null)
            throw new InvalidOperationException(
                $"No STT provider available. Selected: {preferred}. Reason: {reason}");

        if (preferred != EchoSettings.SttEngine.WhisperBase && preferred != EchoSettings.SttEngine.WhisperSmallEn && pick != _whisper)
        {
            try
            {
                return await pick.TranscribeAsync(samples16k, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Primary provider {pick.Name} failed ({ex.Message}). Falling back to Whisper.");
                if (_whisper.IsLoaded)
                    return await _whisper.TranscribeAsync(samples16k, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        return await _whisper.TranscribeAsync(samples16k, cancellationToken).ConfigureAwait(false);
    }

    private ISttProvider Resolve(out EchoSettings.SttEngine preferred, out string reason)
    {
        preferred = _activeEngine();
        reason = string.Empty;

        switch (preferred)
        {
            case EchoSettings.SttEngine.Parakeet:
                if (_parakeet.IsLoaded) return _parakeet;
                reason = "Parakeet selected but model not downloaded.";
                Logger.Warn($"{reason} Falling back to Whisper.");
                return _whisper.IsLoaded ? _whisper : null!;

            case EchoSettings.SttEngine.Groq:
                if (!_allowCloud())
                {
                    reason = "Groq selected but no API key configured; using local Whisper.";
                    Logger.Warn(reason);
                    preferred = EchoSettings.SttEngine.WhisperBase;
                    return _whisper.IsLoaded ? _whisper : null!;
                }
                if (_groq.IsLoaded) return _groq;
                reason = "Groq selected but provider not loaded; using local Whisper.";
                Logger.Warn(reason);
                return _whisper.IsLoaded ? _whisper : null!;

            case EchoSettings.SttEngine.WhisperBase:
            case EchoSettings.SttEngine.WhisperSmallEn:
            default:
                return _whisper.IsLoaded ? _whisper : null!;
        }
    }

    public void Dispose()
    {
        // The three providers are owned by the DI container; we don't
        // dispose them here, just clear our references.
    }
}
