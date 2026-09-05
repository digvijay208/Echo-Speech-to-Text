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
    private readonly DictionaryService? _dictionaryService;
    private readonly HistoryService? _historyService;
    private CancellationTokenSource? _downloadCts;

    public SettingsWindow(
        ModelManager modelManager,
        KeyboardHookService keyboardHook,
        AudioCaptureService audioCapture,
        Action<string>? onModelReady = null,
        DictionaryService? dictionaryService = null,
        HistoryService? historyService = null)
    {
        _modelManager = modelManager;
        _keyboardHook = keyboardHook;
        _audioCapture = audioCapture;
        _onModelReady = onModelReady;
        _dictionaryService = dictionaryService;
        _historyService = historyService;

        InitializeComponent();

        Loaded += OnLoaded;
        IsVisibleChanged += (_, args) =>
        {
            // Window is reused, so Loaded only ever fires once. Refresh on each show so a
            // model downloaded elsewhere is reflected here.
            if (args.NewValue is true)
            {
                CheckModelStatus();
                SyncHotkeyCombo();
                SyncSpokenLanguageCombo();
                RefreshVocabPreview();
                RefreshHistoryCount();
            }
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
                        isDownloaded ? (byte)0x16 : (byte)0xDC,
                        isDownloaded ? (byte)0xA3 : (byte)0x26,
                        isDownloaded ? (byte)0x4A : (byte)0x26))
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
                    System.Windows.Media.Color.FromRgb(0x24, 0x26, 0x2B)),
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
                    System.Windows.Media.Color.FromRgb(0x6B, 0x72, 0x80)),
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
        UpdateHotkeyHint();
    }

    /// <summary>
    /// Reflect the live hook key in the dropdown (the window is reused, so the
    /// selection can go stale). Falls back to Right Control.
    /// </summary>
    private void SyncHotkeyCombo()
    {
        if (HotkeyCombo == null) return;
        string current = _keyboardHook != null ? _keyboardHook.TargetVkCode.ToString() : "163";
        foreach (ComboBoxItem item in HotkeyCombo.Items)
        {
            if (item.Tag is string tag && tag == current)
            {
                HotkeyCombo.SelectedItem = item;
                break;
            }
        }
        UpdateHotkeyHint();
    }

    private void UpdateHotkeyHint()
    {
        if (HotkeyHintText == null || HotkeyCombo.SelectedItem is not ComboBoxItem item)
            return;
        string name = (item.Content as string ?? "Right Control").Split(" (")[0];
        HotkeyHintText.Text = $"Hold {name} and speak. Release to transcribe and type anywhere.";
    }

    private void AudioDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_audioCapture != null && AudioDeviceCombo.SelectedItem is ComboBoxItem item && item.Tag is string deviceId)
        {
            _audioCapture.SelectedDeviceId = deviceId;
        }
        if (MicHintText != null && AudioDeviceCombo.SelectedItem is ComboBoxItem selected)
        {
            MicHintText.Text = selected.Content as string ?? "Input device used for dictation.";
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
    private Action? _onLanguageChanged;

    /// <summary>
    /// Called by App.xaml.cs to hand the window the live settings object
    /// (so changes here are visible to the rest of the app) and the shared
    /// GroqProvider (so we can hand it the key without rebuilding it).
    /// </summary>
    public void BindSettings(EchoSettings settings, GroqProvider? groqProvider, Func<bool> vadAvailable, Action onSttEngineChanged, Action? onLanguageChanged = null)
    {
        _settings = settings;
        _groqProvider = groqProvider;
        _vadAvailable = vadAvailable;
        _onSttEngineChanged = onSttEngineChanged;
        _onLanguageChanged = onLanguageChanged;

        // Hydrate controls from persisted values. The dropdown order in
        // XAML must stay in sync with the SttEngine enum:
        //   0 WhisperBase, 1 WhisperSmallEn, 2 Parakeet, 3 Groq.
        SttEngineCombo.SelectedIndex = (int)settings.Engine;
        EnableVadCheckBox.IsChecked = settings.VadEnabled;
        GroqApiKeyBox.Password = settings.GroqApiKey;
        SyncSpokenLanguageCombo();

        UpdateSttEngineStatus();
    }

    private void SpokenLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        string code = "";
        if (SpokenLanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            code = tag;
        if (_settings.SpokenLanguage == code) return;
        _settings.SpokenLanguage = code;
        _settings.Save();
        UpdateSpokenLangHint();
        UpdateSttEngineStatus();
        _onLanguageChanged?.Invoke();
    }

    /// <summary>
    /// Reflect the persisted language in the picker (window is reused).
    /// Unknown codes fall back to Auto-detect.
    /// </summary>
    private void SyncSpokenLanguageCombo()
    {
        if (SpokenLanguageCombo == null) return;
        string current = _settings?.SpokenLanguage ?? "";
        bool matched = false;
        foreach (ComboBoxItem item in SpokenLanguageCombo.Items)
        {
            if (item.Tag is string tag && tag == current)
            {
                SpokenLanguageCombo.SelectedItem = item;
                matched = true;
                break;
            }
        }
        if (!matched) SpokenLanguageCombo.SelectedIndex = 0;
        UpdateSpokenLangHint();
    }

    private void UpdateSpokenLangHint()
    {
        if (SpokenLangHintText == null) return;
        string code = _settings?.SpokenLanguage ?? "";
        if (string.IsNullOrEmpty(code))
        {
            SpokenLangHintText.Text = "Auto-detect: the engine guesses the language each time. Pick one for better accuracy.";
            return;
        }
        string name = SpokenLanguageCombo.SelectedItem is ComboBoxItem item
            ? (item.Content as string ?? code).Split(" (")[0]
            : code;
        SpokenLangHintText.Text = $"Dictating in {name}. Needs Whisper Base or Groq — English-only engines stay on English.";
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

        // Warn when the user picked a non-English language but the engine can't do it.
        string langNote = "";
        if (!string.IsNullOrEmpty(_settings.SpokenLanguage) && _settings.SpokenLanguage != "en" &&
            (_settings.Engine == EchoSettings.SttEngine.WhisperSmallEn || _settings.Engine == EchoSettings.SttEngine.Parakeet))
        {
            langNote = " Note: this engine is English-only — switch to Whisper Base or Groq for that language.";
        }

        switch (_settings.Engine)
        {
            case EchoSettings.SttEngine.WhisperBase:
                SttEngineStatus.Text = "Whisper Base: ~142 MB, runs on any CPU, multilingual.";
                break;

            case EchoSettings.SttEngine.WhisperSmallEn:
                if (_modelManager.IsModelDownloaded("small.en"))
                    SttEngineStatus.Text = "Whisper Small (English): installed. High accuracy on English." + langNote;
                else
                {
                    SttEngineStatus.Text = "Whisper Small (English): not installed (~465 MB)." + langNote;
                    ShowDownloadButton("DOWNLOAD WHISPER SMALL (~465 MB)", isParakeet: false);
                }
                break;

            case EchoSettings.SttEngine.Parakeet:
                if (_modelManager.IsParakeetDownloaded())
                    SttEngineStatus.Text = "Parakeet TDT V3: installed. Fast and accurate on English." + langNote;
                else
                {
                    SttEngineStatus.Text = "Parakeet TDT V3: not installed (~670 MB). Faster than Whisper, English-only." + langNote;
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

    // ─── Chrome ──────────────────────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        if (WindowState != WindowState.Normal) return;
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* ignore races */ }
    }

    // ─── Sidebar nav ─────────────────────────────────────────────────────────

    private void ShowOnly(UIElement visible)
    {
        GeneralPanel.Visibility = visible == GeneralPanel ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = visible == SystemPanel ? Visibility.Visible : Visibility.Collapsed;
        VibePanel.Visibility = visible == VibePanel ? Visibility.Visible : Visibility.Collapsed;
        AccountPanel.Visibility = visible == AccountPanel ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPanel.Visibility = visible == PrivacyPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetActiveNav(System.Windows.Controls.Primitives.ToggleButton active)
    {
        foreach (var btn in new[] { NavGeneralBtn, NavSystemBtn, NavVibeBtn, NavAccountBtn, NavTeamBtn, NavPlansBtn, NavPrivacyBtn })
        {
            if (btn == null) continue;
            btn.IsChecked = btn == active;
        }
    }

    private void NavGeneralBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavGeneralBtn);
        ShowOnly(GeneralPanel);
    }

    private void NavSystemBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavSystemBtn);
        ShowOnly(SystemPanel);
    }

    private void NavVibeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavVibeBtn);
        RefreshVocabPreview();
        ShowOnly(VibePanel);
    }

    private void NavAccountBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavAccountBtn);
        AccountTitle.Text = "Account";
        AccountBody.Text = "Echo runs on your machine. Dictation, history, and vocabulary never leave this PC unless you pick the Groq cloud engine. There is nothing to sign into.";
        ShowOnly(AccountPanel);
    }

    private void NavTeamBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavTeamBtn);
        AccountTitle.Text = "Team";
        AccountBody.Text = "Echo is a single-player desktop tool — no workspaces, no invites, no shared anything. Your Transforms live in %LOCALAPPDATA%\\Echo\\dictionary.json on this PC.";
        ShowOnly(AccountPanel);
    }

    private void NavPlansBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavPlansBtn);
        AccountTitle.Text = "Plans and Billing";
        AccountBody.Text = "There is nothing to pay. Local engines are free forever; Groq's free tier covers cloud transcription with your own API key. No card, no subscription.";
        ShowOnly(AccountPanel);
    }

    private void NavPrivacyBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavPrivacyBtn);
        RefreshHistoryCount();
        ShowOnly(PrivacyPanel);
    }

    // ─── Vibe coding panel ───────────────────────────────────────────────────

    private void RefreshVocabPreview()
    {
        if (VocabPreviewText == null) return;
        if (_dictionaryService == null)
        {
            VocabPreviewText.Text = "Dictionary service not available.";
            return;
        }
        var entries = _dictionaryService.Entries;
        if (VocabCountText != null)
            VocabCountText.Text = $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} — words the recognizer biases toward and auto-corrects. Managed in Transforms.";
        VocabPreviewText.Text = entries.Count == 0
            ? "No entries yet. Add some in Transforms."
            : string.Join("\n", entries.Take(50).Select(e => $"• {e.DisplayRule}"));
    }

    // ─── Privacy panel ───────────────────────────────────────────────────────

    private void RefreshHistoryCount()
    {
        if (HistoryCountText == null) return;
        int count = _historyService?.Entries.Count ?? 0;
        HistoryCountText.Text = count == 0
            ? "No dictations stored."
            : $"{count} dictation{(count == 1 ? "" : "s")} stored on this PC.";
        if (ClearHistoryButton != null)
            ClearHistoryButton.IsEnabled = count > 0;
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_historyService == null || _historyService.Entries.Count == 0) return;
        var result = System.Windows.MessageBox.Show(
            "Delete all stored dictations? This cannot be undone.",
            "Clear history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            _historyService.ClearAll();
            Logger.Info("History cleared from Settings > Data and Privacy.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to clear history", ex);
        }
        RefreshHistoryCount();
    }
}
