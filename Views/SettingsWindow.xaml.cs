using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Echo.Services;
using Echo.Services.Stt;

namespace Echo.Views;

/// <summary>
/// Settings window for configuring hotkey, Whisper model, and audio device.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ModelManager _modelManager;
    private readonly KeyboardHookService _keyboardHook;
    private readonly AudioCaptureService _audioCapture;
    private readonly Action<string>? _onModelReady;
    private CancellationTokenSource? _downloadCts;

    public SettingsWindow(
        ModelManager modelManager,
        KeyboardHookService keyboardHook,
        AudioCaptureService audioCapture,
        Action<string>? onModelReady = null)
    {
        _modelManager = modelManager;
        _keyboardHook = keyboardHook;
        _audioCapture = audioCapture;
        _onModelReady = onModelReady;

        InitializeComponent();

        Loaded += OnLoaded;
        IsVisibleChanged += (_, args) =>
        {
            // Window is reused, so Loaded only ever fires once. Refresh on each show so a
            // model downloaded elsewhere is reflected here.
            if (args.NewValue is true) CheckModelStatus();
        };
        Closing += (_, e) =>
        {
            // Hide instead of close — we reuse this window
            e.Cancel = true;
            CancelDownload();
            Hide();
        };
    }

    private void CancelDownload()
    {
        var cts = _downloadCts;
        _downloadCts = null;
        if (cts == null) return;

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished and disposed — nothing to cancel.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadAudioDevices();
        CheckModelStatus();
        PopulateDownloadedModelsList();
    }

    /// <summary>
    /// Renders one row per known model showing filename, size, and installed/missing state.
    /// Re-populated on every window show so newly-downloaded models appear immediately.
    /// </summary>
    private void PopulateDownloadedModelsList()
    {
        DownloadedModelsList.Children.Clear();
        var entries = _modelManager.GetModelsStatus();

        // Downloaded first, then missing, sorted by file size descending so the user can
        // see the largest model they have at the top.
        foreach (var (name, fileName, sizeMb, isDownloaded) in entries
            .OrderByDescending(e => e.IsDownloaded)
            .ThenByDescending(e => e.SizeMB))
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(20) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            // Status indicator dot.
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(
                        isDownloaded ? (byte)0x52 : (byte)0xE6,
                        isDownloaded ? (byte)0xB7 : (byte)0x39,
                        isDownloaded ? (byte)0x88 : (byte)0x46))
            };
            System.Windows.Controls.Grid.SetColumn(dot, 0);
            row.Children.Add(dot);

            // Filename + tag.
            var label = new TextBlock
            {
                FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontWeight = FontWeights.SemiBold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xF4, 0xF4, 0xF6)),
                Text = $"{name}    {fileName}",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            System.Windows.Controls.Grid.SetColumn(label, 1);
            row.Children.Add(label);

            // Size column.
            var size = new TextBlock
            {
                FontSize = 11,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x9E, 0x9E, 0xA6)),
                Text = isDownloaded ? $"{sizeMb} MB" : "—",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 4, 0)
            };
            System.Windows.Controls.Grid.SetColumn(size, 2);
            row.Children.Add(size);

            DownloadedModelsList.Children.Add(row);
        }
    }

    private void LoadAudioDevices()
    {
        AudioDeviceCombo.Items.Clear();
        var devices = AudioCaptureService.GetAvailableDevices();

        // Default option: auto-resolves to the laptop mic array when present,
        // else the OS default (see AudioCaptureService.FindPreferredDeviceId).
        AudioDeviceCombo.Items.Add(new ComboBoxItem
        {
            Content = "🎯 Default Microphone (auto)",
            Tag = ""
        });

        int selectedIndex = 0;
        int i = 1;
        foreach (var (id, name, isDefault) in devices)
        {
            string label = isDefault ? $"{name} (Default)" : name;
            AudioDeviceCombo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = id
            });
            if (isDefault)
                selectedIndex = i;
            i++;
        }

        AudioDeviceCombo.SelectedIndex = selectedIndex;
    }

    private void CheckModelStatus()
    {
        UpdateSttEngineStatus();
    }

    private void HotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_keyboardHook != null && HotkeyCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
        {
            if (int.TryParse(tagStr, out int vkCode))
            {
                _keyboardHook.TargetVkCode = vkCode;
            }
        }
    }

    private void AudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioCapture != null && AudioDeviceCombo.SelectedItem is ComboBoxItem item && item.Tag is string deviceId)
        {
            _audioCapture.SelectedDeviceId = deviceId;
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelDownload();
        Hide();
    }

    // ========================================================================
    // STT Engine, VAD, and Groq settings
    // ========================================================================

    private EchoSettings? _settings;
    private GroqProvider? _groqProvider;
    private Func<bool>? _vadAvailable;
    private Action? _onSttEngineChanged;

    /// <summary>
    /// Called by App.xaml.cs to hand the window the live settings object
    /// (so changes here are visible to the rest of the app) and the shared
    /// GroqProvider (so we can hand it the key without rebuilding it).
    /// </summary>
    public void BindSettings(EchoSettings settings, GroqProvider? groqProvider, Func<bool> vadAvailable, Action onSttEngineChanged)
    {
        _settings = settings;
        _groqProvider = groqProvider;
        _vadAvailable = vadAvailable;
        _onSttEngineChanged = onSttEngineChanged;

        // Hydrate controls from persisted values. The dropdown order in
        // XAML must stay in sync with the SttEngine enum:
        //   0 WhisperBase, 1 WhisperSmallEn, 2 Parakeet, 3 Groq.
        SttEngineCombo.SelectedIndex = (int)settings.Engine;
        EnableVadCheckBox.IsChecked = settings.VadEnabled;
        GroqApiKeyBox.Password = settings.GroqApiKey;

        UpdateSttEngineStatus();
    }

    private void SttEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        _settings.Engine = (EchoSettings.SttEngine)SttEngineCombo.SelectedIndex;
        _settings.Save();
        UpdateSttEngineStatus();
        _onSttEngineChanged?.Invoke();
    }

    private void UpdateSttEngineStatus()
    {
        if (_settings == null) return;

        DownloadEngineButton.Visibility = Visibility.Collapsed;
        GroqApiKeyBox.IsEnabled = true;

        switch (_settings.Engine)
        {
            case EchoSettings.SttEngine.WhisperBase:
                SttEngineStatus.Text = "Whisper Base: ~142 MB, runs on any CPU, multilingual.";
                break;

            case EchoSettings.SttEngine.WhisperSmallEn:
                if (_modelManager.IsModelDownloaded("small.en"))
                    SttEngineStatus.Text = "Whisper Small (English): installed. High accuracy on English.";
                else
                {
                    SttEngineStatus.Text = "Whisper Small (English): not installed (~465 MB).";
                    ShowDownloadButton("DOWNLOAD WHISPER SMALL (~465 MB)", isParakeet: false);
                }
                break;

            case EchoSettings.SttEngine.Parakeet:
                if (_modelManager.IsParakeetDownloaded())
                    SttEngineStatus.Text = "Parakeet TDT V3: installed. Fast and accurate on English.";
                else
                {
                    SttEngineStatus.Text = "Parakeet TDT V3: not installed (~670 MB). Faster than Whisper, English-only.";
                    ShowDownloadButton("DOWNLOAD PARAKEET V3 (~670 MB)", isParakeet: true);
                }
                break;

            case EchoSettings.SttEngine.Groq:
                GroqApiKeyBox.IsEnabled = true;
                if (!string.IsNullOrEmpty(_settings.GroqApiKey))
                    SttEngineStatus.Text = "Groq Whisper Large V3 Turbo: API key set. Free tier, best accuracy.";
                else
                {
                    SttEngineStatus.Text = "Groq Whisper Large V3 Turbo: paste your free API key below to enable.";
                    GroqApiKeyBox.IsEnabled = true;
                }
                break;
        }
    }

    private void ShowDownloadButton(string label, bool isParakeet)
    {
        DownloadEngineButton.Visibility = Visibility.Visible;
        DownloadEngineButtonText.Text = label;
        DownloadEngineButton.Tag = isParakeet;
    }

    private async void DownloadEngineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_modelManager == null) return;
        bool isParakeet = DownloadEngineButton.Tag is bool b && b;

        DownloadEngineButton.IsEnabled = false;
        DownloadEngineButtonText.Text = "DOWNLOADING...";

        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            if (p.total > 0)
                DownloadEngineButtonText.Text =
                    $"DOWNLOADING {p.downloaded / (1024 * 1024)} / {p.total / (1024 * 1024)} MB";
        });

        try
        {
            if (isParakeet)
                await _modelManager.DownloadParakeetAsync(progress);
            else
                await _modelManager.DownloadModelAsync("small.en", progress);

            Logger.Info(isParakeet ? "Parakeet download complete." : "Whisper Small download complete.");
            DownloadEngineButtonText.Text = "INSTALLED ✓";
            PopulateDownloadedModelsList();
            _onSttEngineChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Error("Engine download failed", ex);
            DownloadEngineButtonText.Text = "RETRY";
            DownloadEngineButton.IsEnabled = true;
        }
        UpdateSttEngineStatus();
    }

    private void EnableVadCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.VadEnabled = EnableVadCheckBox.IsChecked == true;
        _settings.Save();
        Logger.Info($"VAD enabled = {_settings.VadEnabled}");
    }

    private void GroqApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.GroqApiKey = GroqApiKeyBox.Password;
        _settings.Save();
        if (!string.IsNullOrEmpty(_settings.GroqApiKey) && _groqProvider != null)
        {
            try
            {
                _groqProvider.Load(_settings.GroqApiKey);
                Logger.Info("Groq provider loaded.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Groq key invalid: {ex.Message}");
            }
        }
        UpdateSttEngineStatus();
    }

    private void GroqKeyLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open Groq signup URL: {ex.Message}");
        }
        e.Handled = true;
    }
}
