using System.Drawing;
using System.Windows;
using Echo.Native;
using Echo.Services;
using Echo.Services.Audio;
using Echo.Services.Stt;
using Echo.Views;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Echo;

/// <summary>
/// Application entry point.
/// Runs a full desktop application window (1980s Studio Tape Deck) with background tray capability.
/// </summary>
public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private HudOverlayWindow? _hudWindow;
    private SettingsWindow? _settingsWindow;

    private KeyboardHookService? _keyboardHook;
    private AudioCaptureService? _audioCapture;
    private TranscriptionService? _transcription;
    private DictionaryService? _dictionaryService;
    private HistoryService? _historyService;
    private DictationService? _dictation;
    private ModelManager? _modelManager;
    private SileroVadService? _vad;
    private ParakeetProvider? _parakeet;
    private GroqProvider? _groq;
    private SttRouter? _sttRouter;
    private EchoSettings? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception logging
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Error("AppDomain UnhandledException", ex);
        };
        DispatcherUnhandledException += (s, args) =>
        {
            Logger.Error("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        Logger.Info($"=== Echo Starting. Log file: {Logger.LogPath} ===");

        // Initialize services
        _modelManager = new ModelManager();
        _keyboardHook = new KeyboardHookService();
        _audioCapture = new AudioCaptureService();
        _transcription = new TranscriptionService();
        _dictionaryService = new DictionaryService();
        _historyService = new HistoryService();

        // VAD + STT pipeline. VAD is loaded eagerly so a missing model
        // file fails the app start instead of failing the first dictation.
        _vad = LoadVadOrNull();

        _parakeet = new ParakeetProvider();
        if (_modelManager.IsParakeetDownloaded())
        {
            try
            {
                _parakeet.Load(_modelManager.GetParakeetModelDirectory());
                Logger.Info("Parakeet model loaded at startup.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Parakeet failed to load: {ex.Message}. Will fall back to Whisper.");
            }
        }

        _groq = new GroqProvider();

        // Settings: drive the router's behaviour at runtime via the Func
        // callbacks so changes in the Settings window take effect without
        // rebuilding the service graph.
        _settings = EchoSettings.Load();
        if (_settings.Engine == EchoSettings.SttEngine.Groq && !string.IsNullOrEmpty(_settings.GroqApiKey))
        {
            try { _groq.Load(_settings.GroqApiKey); }
            catch (Exception ex) { Logger.Warn($"Groq key from settings rejected: {ex.Message}"); }
        }

        _sttRouter = new SttRouter(
            _transcription,
            _parakeet,
            _groq,
            activeEngine: () => _settings!.Engine,
            allowCloud: () => _settings!.Engine == EchoSettings.SttEngine.Groq);

        _dictation = new DictationService(
            _keyboardHook,
            _audioCapture,
            _transcription,
            _dictionaryService,
            _historyService,
            vad: _vad,
            sttRouter: _sttRouter,
            vadEnabled: () => _settings!.VadEnabled);

        // Create HUD overlay. The idle Echo Bar pill is visible from launch;
        // dictation states swap it for the active cards, and it returns to the
        // idle pill after each successful dictation.
        _hudWindow = new HudOverlayWindow();
        _hudWindow.SetIdleState();
        _hudWindow.ShowHud();

        // Wire up HUD to dictation service
        _dictation.HudShouldShow += () => Dispatcher.Invoke(() => _hudWindow.ShowHud());
        _dictation.HudShouldHide += () => Dispatcher.Invoke(() => _hudWindow.HideHud());
        _dictation.HudError += msg => Dispatcher.Invoke(() => _hudWindow.SetErrorState(msg));

        // HUD buttons. Undo must wait for the injection to finish before sending
        // backspaces, or it would delete the user's own text instead of ours.
        _hudWindow.UndoRequested += async undoChars =>
        {
            try
            {
                if (_dictation != null) await _dictation.WhenLastInjectionComplete();
                int chars = undoChars;
                await Task.Run(() => TextInjectionService.DeleteLastInjection(chars));
            }
            catch (Exception ex)
            {
                Logger.Error("HUD undo failed", ex);
            }
        };
        _hudWindow.RetryRequested += () => Dispatcher.Invoke(() => _dictation!.StartManualRecording());
        _hudWindow.CancelRequested += () => Dispatcher.Invoke(() => _dictation!.CancelRecording());

        _dictation.PropertyChanged += (_, args) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (args.PropertyName == nameof(DictationService.State))
                {
                    switch (_dictation.State)
                    {
                        case DictationService.DictationState.Recording:
                            _hudWindow.SetRecordingState();
                            break;
                        case DictationService.DictationState.Transcribing:
                            _hudWindow.SetTranscribingState();
                            break;
                        case DictationService.DictationState.Injecting:
                            _hudWindow.SetDoneState(_dictation.TranscriptPreview, _dictation.LastInjectedChars);
                            break;
                        case DictationService.DictationState.Idle:
                            // After a successful dictation the bar stays on screen as
                            // the compact idle pill (failure paths use HudError instead).
                            _hudWindow.SetIdleState();
                            break;
                    }
                }
                else if (args.PropertyName == nameof(DictationService.AudioLevel))
                {
                    _hudWindow.UpdateAudioLevel(_dictation.AudioLevel);
                }
            });
        };

        // Create Settings Window
        _settingsWindow = new SettingsWindow(
            _modelManager,
            _keyboardHook,
            _audioCapture,
            OnModelReady);

        // Bind persisted settings to the new UI so the controls reflect
        // what's on disk and any change persists to settings.json.
        _settingsWindow.BindSettings(
            _settings!,
            _groq,
            vadAvailable: () => _vad != null,
            onSttEngineChanged: () => ApplySttEngineSelection());

        // Create and Show Main Desktop Window
        _mainWindow = new MainWindow(
            _dictation,
            _historyService,
            _dictionaryService,
            ShowSettings);

        MainWindow = _mainWindow;
        _mainWindow.Show();

        // Create system tray icon as secondary status
        SetupTrayIcon();

        // Auto-load best available model on disk
        TryLoadModel();

        // Start the keyboard hook for global push-to-talk. A hook failure must not take the
        // whole app down — the window still works via the on-screen REC button.
        try
        {
            _dictation.Start();
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start global keyboard hook", ex);
            MessageBox.Show(
                $"Could not install the global push-to-talk hook:\n\n{ex.Message}\n\n" +
                "You can still record with the on-screen REC button.",
                "ECHO Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Visible = true,
            Text = "ECHO TC-80 — Speech Dictation Deck"
        };

        var contextMenu = new Forms.ContextMenuStrip();

        var titleItem = new Forms.ToolStripMenuItem("📼 ECHO TC-80")
        {
            Enabled = false,
            Font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
        };
        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());

        var showItem = new Forms.ToolStripMenuItem("🎛 Show Main Deck");
        showItem.Click += (_, _) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        var settingsItem = new Forms.ToolStripMenuItem("⚙ Settings (Ctrl+,)");
        settingsItem.Click += (_, _) => ShowSettings();
        contextMenu.Items.Add(settingsItem);

        var aboutItem = new Forms.ToolStripMenuItem("ℹ About");
        aboutItem.Click += (_, _) => ShowAbout();
        contextMenu.Items.Add(aboutItem);

        contextMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Dark industrial background
        using var bgBrush = new SolidBrush(Color.FromArgb(28, 29, 33));
        g.FillRectangle(bgBrush, 2, 2, 28, 28);

        // Aluminum border
        using var borderPen = new Pen(Color.FromArgb(226, 226, 230), 1.5f);
        g.DrawRectangle(borderPen, 2, 2, 27, 27);

        // Red Record LED
        using var redBrush = new SolidBrush(Color.FromArgb(214, 40, 40));
        g.FillEllipse(redBrush, 12, 12, 8, 8);

        // GetHicon() hands back an unmanaged HICON that Icon.FromHandle does NOT own.
        // Clone into a self-contained Icon, then destroy the handle — otherwise it leaks.
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            Win32.DestroyIcon(hIcon);
        }
    }

    public void ShowMainWindow()
    {
        Dispatcher.Invoke(() =>
        {
            if (_mainWindow != null)
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        });
    }

    public void ShowSettings()
    {
        Dispatcher.Invoke(() =>
        {
            if (_settingsWindow != null)
            {
                _settingsWindow.Show();
                _settingsWindow.Activate();
            }
        });
    }

    private void OnModelReady(string modelPath)
    {
        try
        {
            _transcription!.LoadModel(modelPath);
            _dictation!.IsModelReady = true;
            _dictation.NotifyModelChanged();

            if (_trayIcon != null)
            {
                _trayIcon.BalloonTipTitle = "Model Ready";
                _trayIcon.BalloonTipText = "Whisper neural model loaded successfully.";
                _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
                _trayIcon.ShowBalloonTip(2000);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load model from {modelPath}", ex);
            MessageBox.Show(
                $"Failed to load model: {ex.Message}",
                "ECHO Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Try to construct a Silero VAD service. If the bundled ONNX file is
    /// missing the service returns null and the app falls back to the
    /// legacy fixed-hold recording path.
    /// </summary>
    private static SileroVadService? LoadVadOrNull()
    {
        try
        {
            return new SileroVadService(VadConfig.Default);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Silero VAD unavailable ({ex.Message}); VAD will be disabled.");
            return null;
        }
    }

    /// <summary>
    /// Apply the user's current STT engine choice. Called from the
    /// Settings window when the dropdown changes; safely idempotent.
    /// </summary>
    private void ApplySttEngineSelection()
    {
        if (_settings == null || _dictation == null) return;
        try
        {
            switch (_settings.Engine)
            {
                case EchoSettings.SttEngine.Parakeet:
                    if (_parakeet != null && !_parakeet.IsLoaded && _modelManager!.IsParakeetDownloaded())
                        _parakeet.Load(_modelManager.GetParakeetModelDirectory());
                    break;
                case EchoSettings.SttEngine.Groq:
                    if (_groq != null && !_groq.IsLoaded && !string.IsNullOrEmpty(_settings.GroqApiKey))
                        _groq.Load(_settings.GroqApiKey);
                    break;
            }
            TryLoadModel();
            _dictation.NotifyModelChanged();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to switch STT engine: {ex.Message}");
        }
    }

    private void TryLoadModel()
    {
        // Parakeet: skip Whisper load entirely, the recognizer is already up.
        if (_settings?.Engine == EchoSettings.SttEngine.Parakeet && _parakeet != null && _parakeet.IsLoaded)
        {
            _dictation!.IsModelReady = true;
            _dictation.NotifyModelChanged();
            Logger.Info("Auto-loaded: Parakeet TDT V3 (per user settings).");
            return;
        }

        // Groq: no model to load on disk; mark ready if the key is set.
        if (_settings?.Engine == EchoSettings.SttEngine.Groq)
        {
            if (_groq != null && _groq.IsLoaded)
            {
                _dictation!.IsModelReady = true;
                _dictation.NotifyModelChanged();
                Logger.Info("Auto-loaded: Groq Whisper Large V3 Turbo (per user settings).");
                return;
            }
            Logger.Warn("Groq selected but no API key; falling back to Whisper.");
        }

        // Whisper variant (or Parakeet/Groq unavailable): pick the best .bin on disk.
        string targetModel = _settings?.Engine == EchoSettings.SttEngine.WhisperSmallEn
            ? "small.en"
            : "base";
        string? available = _modelManager!.GetFirstAvailableModel();
        if (available == null || !_modelManager.IsModelDownloaded(targetModel))
        {
            // The user-selected Whisper variant isn't downloaded. Try the
            // configured one, then fall back to whatever else is on disk.
            string modelPath = _modelManager.GetModelPath(targetModel);
            try
            {
                _transcription!.LoadModel(modelPath);
                _dictation!.IsModelReady = true;
                Logger.Info($"Auto-loaded Whisper: '{targetModel}' from {modelPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to auto-load model '{targetModel}'", ex);
                if (available != null)
                {
                    try
                    {
                        _transcription!.LoadModel(_modelManager.GetModelPath(available));
                        _dictation!.IsModelReady = true;
                        Logger.Info($"Fell back to '{available}'.");
                    }
                    catch (Exception ex2)
                    {
                        Logger.Error($"Fallback model '{available}' also failed", ex2);
                    }
                }
            }
        }
        else
        {
            string modelPath = _modelManager.GetModelPath(targetModel);
            try
            {
                _transcription!.LoadModel(modelPath);
                _dictation!.IsModelReady = true;
                Logger.Info($"Auto-loaded model: '{targetModel}' from {modelPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to auto-load model '{targetModel}'", ex);
            }
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "ECHO MODEL TC-80\n" +
            "Portable Speech Recorder & Push-to-Talk Dictation Deck\n\n" +
            "• On-device speech recognition via Whisper.net\n" +
            "• Dual-pass custom terminology dictionary\n" +
            "• 1980s Hi-Fi instrumentation & ballistic VU meter\n" +
            "• Zero cloud telemetry / 100% private offline\n\n" +
            "Hold RIGHT CTRL in any app to record and type.",
            "About ECHO TC-80",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitApplication()
    {
        Logger.Info("Exit requested; shutting down.");

        _dictation?.Dispose();
        _trayIcon?.Dispose();
        _trayIcon = null;
        _hudWindow?.Close();
        _settingsWindow?.Close();

        // MainWindow cancels Closing to minimise to tray, so it must be told this is a
        // real exit or it would simply hide itself here.
        if (_mainWindow != null)
        {
            _mainWindow.AllowClose = true;
            _mainWindow.Close();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _dictation?.Dispose();
        _vad?.Dispose();
        _parakeet?.Dispose();
        _groq?.Dispose();
        base.OnExit(e);
    }
}
