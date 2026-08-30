using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Echo.Models;
using Echo.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;

namespace Echo.Views;

/// <summary>
/// 1980s Studio / Field Recorder Main Window Interface.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DictationService _dictationService;
    private readonly HistoryService _historyService;
    private readonly DictionaryService _dictionaryService;
    private readonly Action _openSettingsAction;

    private readonly DispatcherTimer _tapeTimer;
    private DateTime _recordStartTime;
    private string? _editingDictId;
    private bool _uiWired;

    /// <summary>
    /// Set by App on a real exit. Closing is otherwise cancelled so the window minimises to tray.
    /// </summary>
    public bool AllowClose { get; set; }

    public MainWindow(
        DictationService dictationService,
        HistoryService historyService,
        DictionaryService dictionaryService,
        Action openSettingsAction)
    {
        _dictationService = dictationService;
        _historyService = historyService;
        _dictionaryService = dictionaryService;
        _openSettingsAction = openSettingsAction;

        InitializeComponent();

        // 10Hz Tape Counter Timer
        _tapeTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _tapeTimer.Tick += OnTapeTimerTick;

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire more than once if the window is re-parented; subscribing twice
        // would double every UI update.
        if (_uiWired) return;
        _uiWired = true;

        // Subscribe to Dictation events
        _dictationService.PropertyChanged += OnDictationPropertyChanged;

        // Subscribe to History and Dictionary data changes
        _historyService.HistoryChanged += () => Dispatcher.Invoke(RefreshHistoryList);
        _dictionaryService.DictionaryChanged += () => Dispatcher.Invoke(RefreshDictionaryList);

        RefreshHistoryList();
        RefreshDictionaryList();
        UpdateTransportState(_dictationService.State);
        UpdateTabIndicators(activeIsHistory: true);
        ActiveModelLabel.Text = _dictationService.ActiveModelName;
        DeckStatusLabel.Text = _dictationService.StatusText.ToUpperInvariant();
    }

    private void OnDictationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnDictationPropertyChanged(sender, args)));
            return;
        }

        if (args.PropertyName == nameof(DictationService.State))
        {
            UpdateTransportState(_dictationService.State);
        }
        else if (args.PropertyName == nameof(DictationService.AudioLevel))
        {
            VuMeter.SetAudioLevel(_dictationService.AudioLevel);
        }
        else if (args.PropertyName == nameof(DictationService.StatusText))
        {
            DeckStatusLabel.Text = _dictationService.StatusText.ToUpperInvariant();
        }
        else if (args.PropertyName == nameof(DictationService.ActiveModelName))
        {
            ActiveModelLabel.Text = _dictationService.ActiveModelName;
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AllowClose) return;

        // Minimize to tray on close instead of exiting
        e.Cancel = true;
        Hide();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Cmd+, / Ctrl+, shortcut to open Settings
        if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _openSettingsAction();
            e.Handled = true;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _openSettingsAction();
    }

    private void UpdateTransportState(DictationService.DictationState state)
    {
        // Buttons and LEDs are driven here from the state machine. Deck status TEXT is
        // driven by the service's StatusText property (see OnDictationPropertyChanged)
        // so that a service-level message like "NO MODEL — OPEN SETTINGS" or
        // "MIC ERROR — CHECK INPUT DEVICE" is not clobbered by a hardcoded string.
        switch (state)
        {
            case DictationService.DictationState.Recording:
                RecButtonLabel.Text = "� RECORDING";
                RecButtonLed.Fill = System.Windows.Media.Brushes.Red;
                RecordButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                _recordStartTime = DateTime.Now;
                _tapeTimer.Start();
                break;

            case DictationService.DictationState.Transcribing:
                RecButtonLabel.Text = "� REC";
                RecButtonLed.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x0A, 0x0C));
                RecordButton.IsEnabled = false;
                StopButton.IsEnabled = false;
                _tapeTimer.Stop();
                VuMeter.Reset();
                break;

            case DictationService.DictationState.Injecting:
                // No button or LED changes for Injecting — service drives status text.
                break;

            case DictationService.DictationState.Idle:
                RecButtonLabel.Text = "� REC";
                RecButtonLed.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0x0A, 0x0C));
                RecordButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                _tapeTimer.Stop();
                VuMeter.Reset();
                break;
        }
    }

    private void OnTapeTimerTick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _recordStartTime;
        TapeCounterText.Text = $"{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100:0}";
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        // Manual Start
        _dictationService.StartManualRecording();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        // Manual Stop
        _dictationService.StopManualRecording();
    }

    // ─── TAB NAVIGATION ────────────────────────────────────────────────────────

    private void TabHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        HistoryViewPanel.Visibility = Visibility.Visible;
        DictionaryViewPanel.Visibility = Visibility.Collapsed;
        UpdateTabIndicators(activeIsHistory: true);
    }

    private void TabDictionaryBtn_Click(object sender, RoutedEventArgs e)
    {
        HistoryViewPanel.Visibility = Visibility.Collapsed;
        DictionaryViewPanel.Visibility = Visibility.Visible;
        UpdateTabIndicators(activeIsHistory: false);
    }

    private void UpdateTabIndicators(bool activeIsHistory)
    {
        if (TabHistoryIndicator != null)
            TabHistoryIndicator.Visibility = activeIsHistory ? Visibility.Visible : Visibility.Collapsed;
        if (TabDictionaryIndicator != null)
            TabDictionaryIndicator.Visibility = activeIsHistory ? Visibility.Collapsed : Visibility.Visible;
    }

    // ─── HISTORY LOG LOGIC ────────────────────────────────────────────────────

    private void RefreshHistoryList()
    {
        // TextChanged fires while InitializeComponent is still building the tree, so the
        // list box may not exist yet.
        if (HistoryListBox == null || HistorySearchBox == null) return;

        // Remember the topmost visible entry by id; reassigning ItemsSource resets scroll
        // to zero, so we re-scroll to the same item after the new template is generated.
        var scroll = FindVisualChild<ScrollViewer>(HistoryListBox);
        string? topId = GetTopVisibleItemId<HistoryEntry>(HistoryListBox, scroll);

        string query = HistorySearchBox.Text;
        var items = _historyService.Search(query);
        HistoryListBox.ItemsSource = items;

        if (topId != null && scroll != null)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollToEntry<HistoryEntry>(HistoryListBox, scroll, topId)),
                DispatcherPriority.Background);
        }
    }

    private void HistorySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HistorySearchPlaceholder != null)
        {
            HistorySearchPlaceholder.Visibility = string.IsNullOrEmpty(HistorySearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        RefreshHistoryList();
    }

    private void ClearHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Are you sure you want to clear all transcription tape history?",
            "ECHO Tape History", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _historyService.ClearAll();
        }
    }

    private async void CopyTranscriptBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string text)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                if (btn.Content is TextBlock tb)
                {
                    // Restore the original brush, not a hardcoded white — the button's
                    // foreground comes from the style and is not necessarily white.
                    string prev = tb.Text;
                    var prevBrush = tb.Foreground;
                    tb.Text = "COPIED ✓";
                    tb.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x52, 0xB7, 0x88));
                    await Task.Delay(1200);
                    tb.Text = prev;
                    tb.Foreground = prevBrush;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Clipboard copy error", ex);
            }
        }
    }

    // ─── DICTIONARY LOGIC ─────────────────────────────────────────────────────

    private void RefreshDictionaryList()
    {
        if (DictionaryListBox == null || DictSearchBox == null) return;

        var scroll = FindVisualChild<ScrollViewer>(DictionaryListBox);
        string? topId = GetTopVisibleItemId<DictionaryEntry>(DictionaryListBox, scroll);

        string query = DictSearchBox.Text;
        var entries = _dictionaryService.Entries;

        if (!string.IsNullOrWhiteSpace(query))
        {
            entries = entries.Where(e =>
                e.From.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.To.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        DictionaryListBox.ItemsSource = entries;

        if (topId != null && scroll != null)
        {
            Dispatcher.BeginInvoke(new Action(() => ScrollToEntry<DictionaryEntry>(DictionaryListBox, scroll, topId)),
                DispatcherPriority.Background);
        }
    }

    private void DictSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DictSearchPlaceholder != null)
        {
            DictSearchPlaceholder.Visibility = string.IsNullOrEmpty(DictSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        RefreshDictionaryList();
    }

    private void AddDictionaryBtn_Click(object sender, RoutedEventArgs e)
    {
        _editingDictId = null;
        ModalTitleText.Text = "ADD DICTIONARY ENTRY";
        RadioCorrection.IsChecked = true;
        ModalFromBox.Text = "";
        ModalToBox.Text = "";
        ModalGluedCheck.IsChecked = true;
        CollisionWarningBox.Visibility = Visibility.Collapsed;
        // Setting IsChecked to a value it already has raises no Checked event, so the
        // TO field / glued checkbox would stay hidden from a previous keyword edit.
        ApplyModalTypeVisibility();
        DictModalOverlay.Visibility = Visibility.Visible;
    }

    private void EditDictEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var entry = _dictionaryService.Entries.FirstOrDefault(x => x.Id == id);
            if (entry != null)
            {
                _editingDictId = entry.Id;
                ModalTitleText.Text = "EDIT DICTIONARY ENTRY";

                if (entry.IsKeywordOnly)
                    RadioKeyword.IsChecked = true;
                else
                    RadioCorrection.IsChecked = true;

                ModalFromBox.Text = entry.From;
                ModalToBox.Text = entry.To;
                ModalGluedCheck.IsChecked = entry.MatchGluedWords;

                ApplyModalTypeVisibility();
                UpdateCollisionWarning();
                DictModalOverlay.Visibility = Visibility.Visible;
            }
        }
    }

    private void DeleteDictEntryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            if (MessageBox.Show("Delete this dictionary entry?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _dictionaryService.Delete(id);
            }
        }
    }

    private void RadioType_Checked(object sender, RoutedEventArgs e)
    {
        ApplyModalTypeVisibility();
    }

    /// <summary>
    /// Shows/hides the TO field and glued-word option for the current entry type.
    /// Guards every control: Checked fires during InitializeComponent, before the rest of
    /// the modal's named elements have been created.
    /// </summary>
    private void ApplyModalTypeVisibility()
    {
        if (RadioCorrection == null || RadioKeyword == null) return;
        if (ModalToSection == null || ModalGluedCheck == null || LabelFromText == null) return;

        bool isCorrection = RadioCorrection.IsChecked == true;
        ModalToSection.Visibility = isCorrection ? Visibility.Visible : Visibility.Collapsed;
        ModalGluedCheck.Visibility = isCorrection ? Visibility.Visible : Visibility.Collapsed;
        LabelFromText.Text = isCorrection ? "WHEN YOU HEAR (SPOKEN PHRASE):" : "VOCABULARY KEYWORD (TO BIAS ENGINE):";
    }

    private void ModalFromBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCollisionWarning();
    }

    private void UpdateCollisionWarning()
    {
        if (CollisionWarningBox == null || CollisionWarningText == null || ModalFromBox == null) return;

        if (RadioCorrection?.IsChecked == true)
        {
            string? warning = DictionaryService.CheckCollisionWarning(ModalFromBox.Text);
            if (!string.IsNullOrEmpty(warning))
            {
                CollisionWarningText.Text = warning;
                CollisionWarningBox.Visibility = Visibility.Visible;
                return;
            }
        }
        CollisionWarningBox.Visibility = Visibility.Collapsed;
    }

    private void ModalSaveBtn_Click(object sender, RoutedEventArgs e)
    {
        string from = ModalFromBox.Text.Trim();
        string to = ModalToBox.Text.Trim();
        bool isKeyword = RadioKeyword.IsChecked == true;

        if (string.IsNullOrWhiteSpace(from))
        {
            MessageBox.Show("Please enter the word or phrase.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!isKeyword && string.IsNullOrWhiteSpace(to))
        {
            MessageBox.Show("Please enter the replacement text.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existing = _editingDictId == null
            ? null
            : _dictionaryService.Entries.FirstOrDefault(x => x.Id == _editingDictId);

        var entry = new DictionaryEntry
        {
            Id = _editingDictId ?? Guid.NewGuid().ToString("N")[..8],
            From = from,
            To = isKeyword ? "" : to,
            IsKeywordOnly = isKeyword,
            MatchGluedWords = ModalGluedCheck.IsChecked == true,
            // Editing must not reset the creation timestamp — the list is ordered by it.
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
        };

        _dictionaryService.AddOrUpdate(entry);
        _editingDictId = null;
        DictModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void ModalCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        _editingDictId = null;
        DictModalOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenDictFileBtn_Click(object sender, RoutedEventArgs e)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Echo");
        string file = Path.Combine(dir, "dictionary.json");

        try
        {
            if (File.Exists(file))
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to open dictionary file", ex);
        }
    }

    // ─── SCROLL RESTORATION HELPERS ───────────────────────────────────────────
    // Reassigning ItemsSource makes WPF re-template every container and resets the
    // ScrollViewer offset to 0. We snapshot the id of the topmost item before the swap
    // and re-scroll to it after the new template is generated, so a search keystroke
    // doesn't throw the user back to the top of the list.

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var deeper = FindVisualChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    private static string? GetTopVisibleItemId<T>(ListBox listBox, ScrollViewer? scroll) where T : class
    {
        if (scroll == null) return null;
        double top = scroll.VerticalOffset;
        foreach (var item in listBox.Items)
        {
            if (item is not T entry) continue;
            var container = listBox.ItemContainerGenerator.ContainerFromItem(entry) as FrameworkElement;
            if (container == null) continue;
            var transform = container.TransformToAncestor(scroll);
            var pos = transform.Transform(new Point(0, 0));
            if (pos.Y + container.ActualHeight >= 0 && pos.Y <= scroll.ViewportHeight + top)
            {
                return GetEntryId(item);
            }
        }
        return null;
    }

    private static string? GetEntryId(object entry)
    {
        return entry switch
        {
            HistoryEntry h => h.Id,
            DictionaryEntry d => d.Id,
            _ => null
        };
    }

    private static void ScrollToEntry<T>(ListBox listBox, ScrollViewer scroll, string id) where T : class
    {
        foreach (var item in listBox.Items)
        {
            if (item is not T entry) continue;
            if (GetEntryId(item) != id) continue;
            var container = listBox.ItemContainerGenerator.ContainerFromItem(entry) as FrameworkElement;
            if (container == null) return;
            var transform = container.TransformToAncestor(scroll);
            var pos = transform.Transform(new Point(0, 0));
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + pos.Y);
            return;
        }
    }
}
