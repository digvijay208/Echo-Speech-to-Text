using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Echo.Models;

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
        HistoryService historyService)
    {
        _keyboardHook = keyboardHook;
        _audioCapture = audioCapture;
        _transcription = transcription;
        _dictionaryService = dictionaryService;
        _historyService = historyService;

        _keyboardHook.PushToTalkPressed += OnPushToTalkPressed;
        _keyboardHook.PushToTalkReleased += OnPushToTalkReleased;
        _audioCapture.AudioLevelChanged += OnAudioLevelChanged;
        _dictionaryService.DictionaryChanged += ApplyBiasingPrompt;
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

        HudShouldShow?.Invoke();
        Logger.Info("Audio recording started.");
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

        try
        {
            // Stop recording and get audio samples
            float[] samples = _audioCapture.StopRecording();
            Logger.Info($"Audio recording stopped. Captured {samples.Length} samples ({samples.Length / 16000.0:F2}s).");

            if (samples.Length < 8000) // Less than 0.5s of audio at 16kHz
            {
                Logger.Warn($"Audio recording was too short ({samples.Length} samples). Ignoring.");
                State = DictationState.Idle;
                StatusText = "TOO SHORT (HOLD LONGER)";
                await Task.Delay(1200);
                StatusText = "STANDBY / READY";
                HudShouldHide?.Invoke();
                return;
            }

            // Transcribe
            State = DictationState.Transcribing;
            StatusText = "TRANSCRIBING...";

            Logger.Info("Starting Whisper transcription...");
            string rawTranscript = await _transcription.TranscribeAsync(samples);
            Logger.Info($"Raw transcript received: \"{rawTranscript}\"");

            // Clean up speech disfluencies & punctuation
            string cleanText = TextCleanupService.Cleanup(rawTranscript);

            if (string.IsNullOrWhiteSpace(cleanText))
            {
                Logger.Warn("Whisper produced empty or blank transcript.");
                State = DictationState.Idle;
                StatusText = "NO SPEECH DETECTED";
                await Task.Delay(1200);
                StatusText = "STANDBY / READY";
                HudShouldHide?.Invoke();
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
            TextInjectionService.InjectText(finalText);

            _historyService.AddEntry(rawTranscript, finalText, duration, appliedCorrections);

            await Task.Delay(800);
        }
        catch (Exception ex)
        {
            Logger.Error("Error during transcription or injection", ex);
            StatusText = $"ERROR: {ex.Message}";
            TranscriptPreview = "";
            await Task.Delay(2000);
        }

        State = DictationState.Idle;
        StatusText = "STANDBY / READY";
        HudShouldHide?.Invoke();
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
