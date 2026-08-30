using System.Drawing;
using System.Windows;
using Echo.Native;
using Echo.Services;
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

        _dictation = new DictationService(
            _keyboardHook,
            _audioCapture,
            _transcription,
            _dictionaryService,
            _historyService);

        // Create HUD overlay (hidden initially, shown on push-to-talk in other apps)
        _hudWindow = new HudOverlayWindow();

        // Wire up HUD to dictation service
        _dictation.HudShouldShow += () => Dispatcher.Invoke(() => _hudWindow.ShowHud());
        _dictation.HudShouldHide += () => Dispatcher.Invoke(() => _hudWindow.HideHud());

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
                            _hudWindow.SetDoneState(_dictation.TranscriptPreview);
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

    private void TryLoadModel()
    {
        string? available = _modelManager!.GetFirstAvailableModel();
        if (available != null)
        {
            string modelPath = _modelManager.GetModelPath(available);
            try
            {
                _transcription!.LoadModel(modelPath);
                _dictation!.IsModelReady = true;
                Logger.Info($"Auto-loaded model: '{available}' from {modelPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to auto-load model '{available}'", ex);
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
        base.OnExit(e);
    }
}
