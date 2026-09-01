using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Echo.Models;
using Echo.Services.Audio;
using Echo.Services.Stt;

namespace Echo.Services;

/// <summary>
/// State machine that orchestrates the entire dictation flow:
/// Idle ? Recording ? Transcribing ? Injecting ? Idle.
/// Wires keyboard hook, audio capture, transcription, text injection, dictionary, and history.
/// </summary>
public sealed class DictationService : INotifyPropertyChanged, IDisposable
{
    public enum DictationState
    {
        Idle,
        Recording,
        Transcribing,
        Injecting
    }

    private readonly KeyboardHookService _keyboardHook;
    private readonly AudioCaptureService _audioCapture;
    private readonly TranscriptionService _transcription;
    private readonly DictionaryService _dictionaryService;
    private readonly HistoryService _historyService;
    private readonly SileroVadService? _vad;
    private readonly ISttProvider? _sttRouter;
    private readonly Func<bool> _vadEnabled;

    // VAD-driven capture: chunks pour in while the user holds the key, and
    // the service raises SegmentReady when a complete utterance has been
    // detected (or when we Flush on key release).
    private readonly List<VadSegment> _capturedSegments = new();
    private bool _vadSubscribed;

    private DictationState _state = DictationState.Idle;
    private string _statusText = "STANDBY / READY";
    private string _transcriptPreview = "";
    private float _audioLevel;
    private bool _isModelReady;
    private DateTime _recordingStartTime;
    private bool _disposed;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? HudShouldShow;
    public event Action? HudShouldHide;

    /// <summary>
    /// Raised when a dictation attempt fails (no speech, too short, engine or
    /// injection error). The HUD stays visible in its Error card with a Retry
    /// button instead of auto-hiding. Always raised with the state already
    /// back at Idle so Retry can start a fresh recording immediately.
    /// </summary>
    public event Action<string>? HudError;

    /// <summary>Character count of the most recent injected text (for HUD Undo).</summary>
    public int LastInjectedChars { get; private set; }

    private Task? _lastInjectionTask;

    /// <summary>Completes when the last text injection has finished sending its
    /// keystrokes. Undo must await this or backspaces race the injected text.</summary>
    public Task WhenLastInjectionComplete() => _lastInjectionTask ?? Task.CompletedTask;

    public DictationState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string TranscriptPreview
    {
        get => _transcriptPreview;
        private set { _transcriptPreview = value; OnPropertyChanged(); }
    }

    public float AudioLevel
    {
        get => _audioLevel;
        private set { _audioLevel = value; OnPropertyChanged(); }
    }

    public bool IsModelReady
    {
        get => _isModelReady;
        set
        {
            if (_isModelReady == value) return;
            _isModelReady = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveModelName));

            if (value)
            {
                ApplyBiasingPrompt();
                if (State == DictationState.Idle)
                    StatusText = "STANDBY / READY";
            }
        }
    }

    /// <summary>
    /// Display name of the Whisper model currently in memory, e.g. "BASE.EN", or "NO MODEL".
    /// </summary>
    public string ActiveModelName
    {
        get
        {
            string? path = _transcription.LoadedModelPath;
            if (string.IsNullOrEmpty(path)) return "NO MODEL";

            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("ggml-", StringComparison.OrdinalIgnoreCase))
                name = name[5..];

            return $"WHISPER {name.ToUpperInvariant()}";
        }
    }

    public DictationService(
        KeyboardHookService keyboardHook,
        AudioCaptureService audioCapture,
        TranscriptionService transcription,
        DictionaryService dictionaryService,
        HistoryService historyService,
        SileroVadService? vad = null,
        ISttProvider? sttRouter = null,
        Func<bool>? vadEnabled = null)
    {
        _keyboardHook = keyboardHook;
        _audioCapture = audioCapture;
        _transcription = transcription;
        _dictionaryService = dictionaryService;
        _historyService = historyService;
        _vad = vad;
        _sttRouter = sttRouter;
        _vadEnabled = vadEnabled ?? (() => false);

        _keyboardHook.PushToTalkPressed += OnPushToTalkPressed;
        _keyboardHook.PushToTalkReleased += OnPushToTalkReleased;
        _audioCapture.AudioLevelChanged += OnAudioLevelChanged;
        _dictionaryService.DictionaryChanged += ApplyBiasingPrompt;

        if (_vad != null)
        {
            _vad.SegmentReady += OnVadSegmentReady;
        }
    }

    private static Dispatcher? UiDispatcher => System.Windows.Application.Current?.Dispatcher;

    private void OnAudioLevelChanged(float level)
    {
        // Fired on the audio capture thread ~20x/sec. Must be a POST, not a blocking Invoke:
        // the capture thread cannot afford to wait on the UI thread.
        UiDispatcher?.BeginInvoke(new Action(() => AudioLevel = level), DispatcherPriority.Background);
    }

    public void Start()
    {
        _keyboardHook.Start();
        StatusText = _isModelReady ? "STANDBY / READY" : "NO MODEL ? OPEN SETTINGS";
        ApplyBiasingPrompt();
        Logger.Info($"DictationService started. Model ready: {_isModelReady}");
    }

    public void StartManualRecording()
    {
        BeginPress();
    }

    public void StopManualRecording()
    {
        BeginRelease();
    }

    /// <summary>
    /// Cancel an in-flight push-to-talk recording (HUD ✕ button): discard the
    /// captured audio, return to Idle and hide the HUD. Nothing is transcribed.
    /// Must be called from the UI thread.
    /// </summary>
    public void CancelRecording()
    {
        if (State != DictationState.Recording) return;

        if (_vadSubscribed)
        {
            _audioCapture.ChunkAvailable -= OnAudioChunk;
            _vadSubscribed = false;
        }

        try
        {
            _audioCapture.StopRecording();
        }
        catch (Exception ex)
        {
            Logger.Warn($"CancelRecording: StopRecording failed: {ex.Message}");
        }

        _vad?.Reset();
        lock (_capturedSegments)
        {
            _capturedSegments.Clear();
        }

        State = DictationState.Idle;
        StatusText = "CANCELLED";
        TranscriptPreview = "";
        HudShouldHide?.Invoke();
        Logger.Info("Dictation cancelled from HUD.");
    }

    /// <summary>
    /// Call after a model swap. The IsModelReady setter short-circuits when the value is
    /// unchanged, so swapping model B for model A would otherwise leave the UI showing A.
    /// </summary>
    public void NotifyModelChanged()
    {
        OnPropertyChanged(nameof(ActiveModelName));
        ApplyBiasingPrompt();
    }

    /// <summary>
    /// Pushes the custom dictionary vocabulary into Whisper as a biasing prompt (pass 1 of 2).
    /// Runs off the UI thread because it rebuilds the Whisper processor.
    /// </summary>
    private void ApplyBiasingPrompt()
    {
        if (_disposed || !_isModelReady) return;

        string prompt = _dictionaryService.GenerateBiasingPrompt();
        Task.Run(() =>
        {
            try
            {
                _transcription.SetPrompt(prompt);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to apply dictionary biasing prompt", ex);
            }
        });
    }

    // ??? Hook entry points ????????????????????????????????????????????????????
    // These run inside the low-level keyboard hook callback. Windows removes hooks that
    // take longer than LowLevelHooksTimeout (~300ms by default), and opening a WASAPI
    // device easily costs that much ? so hand off to the UI thread and return immediately.

    private void OnPushToTalkPressed() => BeginPress();

    private void OnPushToTalkReleased() => BeginRelease();

    private void BeginPress()
    {
        var dispatcher = UiDispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(new Action(HandlePress));
    }

    private void BeginRelease()
    {
        var dispatcher = UiDispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(new Action(HandleRelease));
    }

    private void HandlePress()
    {
        Logger.Info($"Push-to-talk pressed. Current state: {State}, IsModelReady: {_isModelReady}");

        if (State != DictationState.Idle)
        {
            Logger.Warn($"Push-to-talk ignored because state is {State}");
            return;
        }

        if (!_isModelReady)
        {
            Logger.Warn("Push-to-talk ignored because Model is not ready. Open Settings to download.");
            StatusText = "NO MODEL ? OPEN SETTINGS";
            return;
        }

        State = DictationState.Recording;
        StatusText = "RECORDING...";
        TranscriptPreview = "";
        AudioLevel = 0;
        _recordingStartTime = DateTime.Now;
        _capturedSegments.Clear();

        try
        {
            _audioCapture.StartRecording();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start audio capture", ex);
            State = DictationState.Idle;
            StatusText = "MIC ERROR ? CHECK INPUT DEVICE";
            return;
        }

        if (!_audioCapture.IsRecording)
        {
            Logger.Error("Audio capture did not start (no usable input device).");
            State = DictationState.Idle;
            StatusText = "MIC ERROR ? CHECK INPUT DEVICE";
            return;
        }

        // Subscribe to the audio capture's per-chunk 16 kHz stream and feed
        // it into the VAD. The VAD will raise SegmentReady on its own thread
        // when it sees a silence gap or max-segment hit; we just collect.
        if (_vad != null && _vadEnabled() && !_vadSubscribed)
        {
            _vad.Reset();
            _audioCapture.ChunkAvailable += OnAudioChunk;
            _vadSubscribed = true;
            Logger.Info("VAD enabled; subscribing to audio chunks.");
        }

        HudShouldShow?.Invoke();
        Logger.Info("Audio recording started.");
    }

    private void OnAudioChunk(float[] chunk16k)
    {
        // VAD is designed around fixed 512-sample chunks. If the capture
        // callback hands us a differently-sized buffer (it does, every
        // 50ms ~ 800 samples at 16 kHz), split it into 512-sample pieces
        // and feed each.
        if (_vad == null) return;
        const int vadChunk = 512;
        for (int i = 0; i + vadChunk <= chunk16k.Length; i += vadChunk)
        {
            var slice = new ReadOnlySpan<float>(chunk16k, i, vadChunk);
            try
            {
                _vad.ProcessChunk(slice);
            }
            catch (Exception ex)
            {
                Logger.Warn($"VAD chunk failed: {ex.Message}");
            }
        }
    }

    private void OnVadSegmentReady(VadSegment segment)
    {
        // VAD callbacks fire on the audio thread. Just stash the segment;
        // HandleRelease drains the list under the UI thread.
        lock (_capturedSegments)
        {
            _capturedSegments.Add(segment);
        }
    }

    private async void HandleRelease()
    {
        Logger.Info($"Push-to-talk released. Current state: {State}");

        if (State != DictationState.Recording)
        {
            Logger.Warn($"Push-to-talk release ignored because state is {State}");
            return;
        }

        double duration = (DateTime.Now - _recordingStartTime).TotalSeconds;

        // Always unsubscribe from the audio stream first; otherwise the
        // capture buffer keeps arriving and the VAD can fire SegmentReady
        // after we have already returned to Idle.
        if (_vadSubscribed)
        {
            _audioCapture.ChunkAvailable -= OnAudioChunk;
            _vadSubscribed = false;
        }

        List<VadSegment> vadSegments = new();
        bool vadInUse = _vad != null && _vadEnabled();

        try
        {
            // Stop recording. With VAD enabled we discard the full-buffer
            // result and only use what the VAD captured, so SilencePad
            // before speech-start doesn't bloat the transcript.
            float[] samples = _audioCapture.StopRecording();
            Logger.Info($"Audio recording stopped. Captured {samples.Length} samples ({samples.Length / 16000.0:F2}s).");

            if (vadInUse)
            {
                // Drain anything the VAD saw during the hold.
                _vad!.Flush();
                lock (_capturedSegments)
                {
                    vadSegments.AddRange(_capturedSegments);
                    _capturedSegments.Clear();
                }
                Logger.Info($"VAD produced {vadSegments.Count} segment(s) totaling {vadSegments.Sum(s => s.SampleCount)} samples.");

                if (vadSegments.Count == 0)
                {
                    Logger.Warn("VAD captured no speech during this push-to-talk.");
                    State = DictationState.Idle;
                    StatusText = "NO SPEECH DETECTED";
                    HudError?.Invoke("Couldn't hear you clearly.");
                    return;
                }
            }
            else if (samples.Length < 8000) // Less than 0.5s of audio at 16kHz
            {
                Logger.Warn($"Audio recording was too short ({samples.Length} samples). Ignoring.");
                State = DictationState.Idle;
                StatusText = "TOO SHORT (HOLD LONGER)";
                HudError?.Invoke("Too short — hold the key a little longer.");
                return;
            }

            // Transcribe
            State = DictationState.Transcribing;
            StatusText = "TRANSCRIBING...";

            string rawTranscript;
            if (vadInUse)
            {
                rawTranscript = await TranscribeVadSegmentsAsync(vadSegments);
            }
            else
            {
                // Route through the STT router so the non-VAD path honours the
                // user's engine choice (Groq/Parakeet/Whisper) exactly like the
                // VAD path does. Previously this called the Whisper service
                // directly, so selecting Groq did nothing when VAD was off.
                Logger.Info($"Starting transcription via {_sttRouter?.Name ?? "Whisper"}...");
                rawTranscript = await (_sttRouter ?? _transcription).TranscribeAsync(samples);
            }
            Logger.Info($"Raw transcript received: \"{rawTranscript}\"");

            // Clean up speech disfluencies & punctuation
            string cleanText = TextCleanupService.Cleanup(rawTranscript);

            if (string.IsNullOrWhiteSpace(cleanText))
            {
                Logger.Warn("Whisper produced empty or blank transcript.");
                State = DictationState.Idle;
                StatusText = "NO SPEECH DETECTED";
                HudError?.Invoke("Couldn't hear you clearly.");
                return;
            }

            // Pass 2: Apply Custom Dictionary post-processing corrections (whole-word, glued-word)
            string finalText = _dictionaryService.ApplyCorrections(cleanText, out var appliedCorrections);

            if (appliedCorrections.Count > 0)
            {
                Logger.Info($"Dictionary corrections applied ({appliedCorrections.Count}): {string.Join(", ", appliedCorrections.Select(c => c.DisplayText))}");
            }

            Logger.Info($"Final text ready: \"{finalText}\"");
            TranscriptPreview = finalText;

            // Inject FIRST. AddEntry serializes and writes history.json synchronously, and
            // the old order made the user wait for that disk write before the text appeared.
            State = DictationState.Injecting;
            StatusText = "DONE ?";
            _lastInjectionTask = TextInjectionService.InjectText(finalText);
            LastInjectedChars = finalText.Length;

            _historyService.AddEntry(rawTranscript, finalText, duration, appliedCorrections);

            // Keep the Inserted card up long enough to read the text and click Undo.
            await Task.Delay(2600);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during transcription or injection", ex);
            StatusText = $"ERROR: {ex.Message}";
            TranscriptPreview = "";
            State = DictationState.Idle;
            HudError?.Invoke("Something went wrong. Try again.");
            return;
        }

        // Success: collapse to the compact idle pill and leave it on screen
        // (per the Echo Bar design) instead of hiding. All failure paths above
        // return early with HudError, so only successful dictations reach here.
        State = DictationState.Idle;
        StatusText = "STANDBY / READY";
    }

    private async Task<string> TranscribeVadSegmentsAsync(List<VadSegment> segments)
    {
        var provider = _sttRouter ?? (ISttProvider)_transcription;
        var combined = new System.Text.StringBuilder();
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            Logger.Info($"Transcribing VAD segment {i + 1}/{segments.Count} ({seg.DurationSeconds:F2}s) via {provider.Name}...");
            string text = await provider.TranscribeAsync(seg.Samples);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (combined.Length > 0) combined.Append(' ');
                combined.Append(text);
            }
        }
        return combined.ToString();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _keyboardHook.PushToTalkPressed -= OnPushToTalkPressed;
        _keyboardHook.PushToTalkReleased -= OnPushToTalkReleased;
        _audioCapture.AudioLevelChanged -= OnAudioLevelChanged;
        _dictionaryService.DictionaryChanged -= ApplyBiasingPrompt;

        _keyboardHook.Dispose();
        _audioCapture.Dispose();
        _transcription.Dispose();
    }
}
