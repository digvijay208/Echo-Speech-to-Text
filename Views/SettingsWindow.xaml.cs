using System.Windows;
using System.Windows.Controls;
using Echo.Services;

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

        // Default option
        AudioDeviceCombo.Items.Add(new ComboBoxItem
        {
            Content = "🎯 Windows Default Microphone",
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
        string modelName = GetSelectedModelName();
        bool downloaded = _modelManager.IsModelDownloaded(modelName);

        if (downloaded)
        {
            ModelStatusText.Text = "✅ MODEL READY & INSTALLED";
            ModelStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x52, 0xB7, 0x88));
            DownloadButton.Content = "INSTALLED ✓";
            DownloadButton.IsEnabled = false;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
        else
        {
            ModelStatusText.Text = "MODEL NOT DOWNLOADED";
            ModelStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE6, 0x39, 0x46));
            DownloadButton.Content = "DOWNLOAD";
            DownloadButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_modelManager != null && ModelStatusText != null)
        {
            CheckModelStatus();
        }
    }

    private string GetSelectedModelName()
    {
        if (ModelCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "base.en";
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        string modelName = GetSelectedModelName();
        DownloadButton.IsEnabled = false;
        DownloadButton.Content = "DOWNLOADING...";
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = false;
        DownloadProgress.Value = 0;

        // Replace (and dispose) any previous token source instead of leaking one per click.
        CancelDownload();
        var cts = new CancellationTokenSource();
        _downloadCts = cts;

        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            // total is -1 when the server sends no Content-Length. Dividing by it produced
            // NaN/Infinity, and ProgressBar.Value rejects those.
            if (p.total > 0)
            {
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = Math.Clamp((double)p.downloaded / p.total * 100, 0, 100);
                ModelStatusText.Text = $"DOWNLOADING: {p.downloaded / (1024 * 1024)} / {p.total / (1024 * 1024)} MB";
            }
            else
            {
                DownloadProgress.IsIndeterminate = true;
                ModelStatusText.Text = $"DOWNLOADING: {p.downloaded / (1024 * 1024)} MB";
            }
        });

        try
        {
            await _modelManager.DownloadModelAsync(modelName, progress, cts.Token);

            ModelStatusText.Text = "✅ MODEL READY & INSTALLED";
            ModelStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x52, 0xB7, 0x88));
            DownloadButton.Content = "INSTALLED ✓";
            DownloadProgress.Visibility = Visibility.Collapsed;

            string modelPath = _modelManager.GetModelPath(modelName);
            _onModelReady?.Invoke(modelPath);
            PopulateDownloadedModelsList();
        }
        catch (OperationCanceledException)
        {
            ModelStatusText.Text = "DOWNLOAD CANCELLED";
            DownloadButton.Content = "DOWNLOAD";
            DownloadButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Logger.Error($"Model download failed: {modelName}", ex);
            ModelStatusText.Text = $"� ERROR: {ex.Message}";
            ModelStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE6, 0x39, 0x46));
            DownloadButton.Content = "RETRY";
            DownloadButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
        finally
        {
            DownloadProgress.IsIndeterminate = false;
            if (ReferenceEquals(_downloadCts, cts))
            {
                _downloadCts = null;
                cts.Dispose();
            }
        }
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
}
