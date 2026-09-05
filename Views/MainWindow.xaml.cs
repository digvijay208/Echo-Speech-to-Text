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
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace Echo.Views;

/// <summary>
/// Light-theme home shell: sidebar + main + right rail.
/// Preserves all original service bindings (dictation, history, dictionary, settings).
/// </summary>
public partial class MainWindow : Window
{
    private readonly DictationService _dictationService;
    private readonly HistoryService _historyService;
    private readonly DictionaryService _dictionaryService;
    private readonly Action _openSettingsAction;
    private readonly EchoSettings _settings;

    private readonly DispatcherTimer _tapeTimer;
    private readonly DispatcherTimer _statsTimer;
    private DateTime _recordStartTime;
    private string? _editingDictId;
    private bool _uiWired;

    // Placement saves are debounced: SizeChanged/LocationChanged fire per-pixel
    // during a drag, and each save is a JSON serialize + file write on the UI
    // thread. Coalesce bursts into one write 600 ms after the last event.
    private readonly DispatcherTimer _placementSaveTimer;
    private static readonly SolidColorBrush RecLedOn = new(System.Windows.Media.Color.FromRgb(0xF4, 0x3F, 0x5E));
    private static readonly SolidColorBrush RecLedOff = new(System.Windows.Media.Color.FromRgb(0x4A, 0x0A, 0x0C));

    // Search boxes rebuild the full list per keystroke; debounce so fast typing
    // does one refresh instead of one per character.
    private readonly DispatcherTimer _historySearchTimer;
    private readonly DispatcherTimer _dictSearchTimer;

    /// <summary>
    /// Set by App on a real exit. Closing is otherwise cancelled so the window minimises to tray.
    /// </summary>
    public bool AllowClose { get; set; }

    public MainWindow(
        DictationService dictationService,
        HistoryService historyService,
        DictionaryService dictionaryService,
        Action openSettingsAction,
        EchoSettings settings)
    {
        _dictationService = dictationService;
        _historyService = historyService;
        _dictionaryService = dictionaryService;
        _openSettingsAction = openSettingsAction;
        _settings = settings;

        InitializeComponent();

        _tapeTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _tapeTimer.Tick += OnTapeTimerTick;

        // 30s tick keeps the "Resets in" countdown fresh without needing
        // a new dictation to trigger RefreshStats. Cheap: pure in-memory.
        _statsTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _statsTimer.Tick += (_, _) => RefreshStats();

        _placementSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(600)
        };
        _placementSaveTimer.Tick += (_, _) =>
        {
            _placementSaveTimer.Stop();
            try { _settings.Save(); }
            catch (Exception ex) { Logger.Error("Debounced placement save failed", ex); }
        };

        _historySearchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _historySearchTimer.Tick += (_, _) =>
        {
            _historySearchTimer.Stop();
            SafeRefresh(RefreshHistoryList);
        };

        _dictSearchTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _dictSearchTimer.Tick += (_, _) =>
        {
            _dictSearchTimer.Stop();
            SafeRefresh(RefreshDictionaryList);
        };

        Loaded += OnLoaded;
        Closing += OnWindowClosing;
        StateChanged += OnStateChanged;
        SizeChanged += OnSizeChanged;
        LocationChanged += OnLocationChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_uiWired) return;
        _uiWired = true;

        _dictationService.PropertyChanged += OnDictationPropertyChanged;

        _historyService.HistoryChanged += () =>
        {
            // Async hops: this fires on the dictation thread after each injection,
            // and blocking Invokes would stall it behind list rendering.
            Dispatcher.BeginInvoke(RefreshHistoryList, DispatcherPriority.Background);
            Dispatcher.BeginInvoke(RefreshStats, DispatcherPriority.Background);
        };
        _dictionaryService.DictionaryChanged += () => Dispatcher.BeginInvoke(RefreshDictionaryList, DispatcherPriority.Background);

        // Each refresh is wrapped so a single failing template can't block
        // the rest of the load. Critical because the history-row template
        // uses Run-binding properties; if WPF can't materialise it (e.g. a
        // bad binding), the rest of the UI must still come up.
        SafeRefresh(RefreshHistoryList);
        SafeRefresh(RefreshDictionaryList);
        SafeRefresh(RefreshStats);
        SafeRefresh(() => UpdateTransportState(_dictationService.State));
        ActiveModelLabel.Text = ResolveActiveModelLabel();
        DeckStatusLabel.Text = _dictationService.StatusText.ToUpperInvariant();

        // Restore last window placement if the saved values are still on a
        // connected display. The chromeless window has no OS memory of its
        // own, so without this the app would always open at 1600x900 centered.
        SafeRefresh(RestoreWindowPlacement);

        // Start the periodic "Resets in" refresher now that the controls
        // are measured (the progress bar needs ActualWidth to size itself).
        _statsTimer.Start();
    }

    /// <summary>
    /// Run a UI initialiser inside a try/catch so a single failing piece
    /// (e.g. a broken template binding) can't take down the whole window.
    /// </summary>
    private void SafeRefresh(Action action)
    {
        try { action(); }
        catch (Exception ex) { Logger.Error("MainWindow initialiser failed", ex); }
    }

    /// <summary>
    /// Compute the titlebar model label, accounting for the active engine
    /// (Groq / Parakeet / local Whisper). The service's ActiveModelName
    /// only knows about local Whisper, so we resolve it here from settings
    /// for cloud and Parakeet paths.
    /// </summary>
    private string ResolveActiveModelLabel()
    {
        if (_settings == null) return "—";

        return _settings.Engine switch
        {
            EchoSettings.SttEngine.Groq        => "GROQ WHISPER V3",
            EchoSettings.SttEngine.Parakeet    => "PARAKEET TDT V3",
            EchoSettings.SttEngine.WhisperSmallEn => "WHISPER SMALL.EN",
            _ => _dictationService.ActiveModelName
        };
    }

    /// <summary>
    /// Recompute every value on the right-rail from the live
    /// <see cref="HistoryService"/> + <see cref="EchoSettings"/>:
    /// total words, average WPM, current day-streak, the Daily Goal ring
    /// + count + target, and the Voice Profile progress + remaining
    /// words-to-unlock. Cheap (pure in-memory over the history snapshot).
    /// </summary>
    private void RefreshStats()
    {
        if (TotalWordsValue == null) return; // XAML not loaded yet

        var entries = _historyService.Entries;
        var today = DateTime.Today;

        // 1) Totals + average WPM
        int totalWords = 0;
        double totalSeconds = 0;
        int todayWords = 0;
        var daysWithDictation = new HashSet<DateTime>();

        foreach (var e in entries)
        {
            int w = e.WordCount;
            totalWords += w;
            totalSeconds += e.DurationSeconds;
            if (e.Timestamp.Date == today) todayWords += w;
            daysWithDictation.Add(e.Timestamp.Date);
        }

        TotalWordsValue.Text = totalWords.ToString("N0");

        // WPM = words / minutes. Guard against div-by-zero on a single
        // very-short entry that hasn't accumulated a real minute yet.
        if (totalSeconds >= 1.0)
        {
            double avgWpm = totalWords / (totalSeconds / 60.0);
            AvgWpmValue.Text = ((int)Math.Round(avgWpm)).ToString("N0");
        }
        else
        {
            AvgWpmValue.Text = "—";
        }

        // 2) Day streak = consecutive days ending today (or yesterday if
        // nothing today yet) on which at least one dictation exists.
        DayStreakValue.Text = ComputeStreak(daysWithDictation, today).ToString();

        // 3) Daily Goal: todayWords / goal → percent + ring + label
        int goal = Math.Max(1, _settings.DailyGoalWords);
        double ratio = Math.Min(1.0, (double)todayWords / goal);
        int percent = (int)Math.Round(ratio * 100);

        DailyGoalPercent.Text = percent + "%";
        DailyGoalCurrentRun.Text = todayWords.ToString("N0");
        DailyGoalTargetRun.Text = " / " + goal.ToString("N0") + " words";

        // The ring's stroke-dash array encodes how much of the circle is
        // filled. 2*pi*r for r=28 is ~175.93; we round to 176 for the
        // XAML baseline. Ratio=1 means "fully filled" (dash == circumference,
        // gap == 0); ratio=0 means "empty" (dash == 0, gap == circumference).
        double circumference = 176.0;
        double filled = circumference * ratio;
        double gap = circumference - filled;
        DailyGoalRing.StrokeDashArray = new DoubleCollection { filled, gap };

        // Encouragement rotates based on the ratio so it feels alive, not
        // pre-canned. Single best line when there's no history at all.
        DailyGoalEncouragement.Text = todayWords switch
        {
            0 => "Press the hotkey or hit Quick Dictation to get started.",
            var w when w < goal * 0.25 => "Off to a start — every word counts.",
            var w when w < goal * 0.5  => "Nice pace. Keep the streak going.",
            var w when w < goal * 0.75 => "Past halfway — you've got this.",
            var w when w < goal        => "Almost there. One more push.",
            _                         => "Goal crushed. Set a bigger one tomorrow."
        };

        // 4) "Resets in" — hours + minutes until local midnight
        var nextMidnight = today.AddDays(1);
        var ts = nextMidnight - DateTime.Now;
        if (ts.TotalSeconds < 0) ts = TimeSpan.Zero; // safety net
        DailyGoalResetsIn.Text = ts.Hours > 0
            ? $"Resets in {ts.Hours}h {ts.Minutes:00}m"
            : $"Resets in {ts.Minutes}m";

        // 5) Voice Profile progress
        int target = Math.Max(1, _settings.VoiceProfileTargetWords);
        double profileRatio = Math.Min(1.0, (double)totalWords / target);

        // The progress Rectangle is Width-bound inside a Grid of 100% width.
        // We compute the actual pixel width off the rendered parent. If
        // the parent hasn't measured yet (e.g. first load race), fall back
        // to a 0 default and let the next refresh correct it.
        double trackWidth = VoiceProfileProgress.Parent is FrameworkElement fe
            ? Math.Max(0, fe.ActualWidth)
            : 0;
        VoiceProfileProgress.Width = trackWidth * profileRatio;
        int remaining = Math.Max(0, target - totalWords);
        VoiceProfileUnlocksText.Text = remaining == 0
            ? "Profile unlocked 🎉"
            : $"Unlocks in {remaining:N0} words";
    }

    /// <summary>
    /// Count the run of consecutive calendar days, ending at <paramref
    /// name="today"/> (or yesterday if no entry today yet), on which a
    /// dictation was recorded. Returns 0 if the most recent entry is older
    /// than yesterday.
    /// </summary>
    private static int ComputeStreak(HashSet<DateTime> daysWithDictation, DateTime today)
    {
        // If there's nothing today, the streak is anchored at yesterday
        // so a 5-day streak doesn't read "0" all day.
        var anchor = daysWithDictation.Contains(today) ? today : today.AddDays(-1);
        if (!daysWithDictation.Contains(anchor)) return 0;

        int streak = 0;
        var cursor = anchor;
        while (daysWithDictation.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }

    /// <summary>
    /// Apply the persisted width/height/top/left. Falls back to the XAML
    /// defaults if anything looks off-screen, too small, or unset.
    /// </summary>
    private void RestoreWindowPlacement()
    {
        // Maximised state is independent of size/position: restore the flag
        // and let WPF compute the size from the current monitor.
        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        if (_settings.WindowWidth > MinWidth &&
            _settings.WindowHeight > MinHeight)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }

        if (_settings.WindowLeft >= 0 && _settings.WindowTop >= 0 &&
            IsOnAnyScreen(_settings.WindowLeft, _settings.WindowTop,
                          _settings.WindowWidth, _settings.WindowHeight))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
    }

    /// <summary>
    /// True if any part of the given rect lands inside a connected display.
    /// Prevents restoring a window onto an unplugged monitor.
    /// </summary>
    private bool IsOnAnyScreen(double left, double top, double width, double height)
    {
        var rect = new Rect(left, top, Math.Max(width, 1), Math.Max(height, 1));
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var bounds = new Rect(
                screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height);
            if (bounds.IntersectsWith(rect)) return true;
        }
        return false;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded || _settings == null) return;
        // Don't capture the size while the user is just maximising —
        // RestoreWindowPlacement reads the maximised flag separately.
        if (WindowState != WindowState.Normal) return;
        _settings.WindowWidth = ActualWidth;
        _settings.WindowHeight = ActualHeight;
        SchedulePlacementSave();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || _settings == null) return;
        if (WindowState != WindowState.Normal) return;
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        SchedulePlacementSave();
    }

    /// <summary>
    /// Restart the debounce timer instead of writing settings.json inline:
    /// a window drag fires dozens of move/resize events per second.
    /// </summary>
    private void SchedulePlacementSave()
    {
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
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
            // No VU meter in the new home shell; the level drives visual feedback in the
            // transport button label and the dictation view state. Hook back up if you
            // reintroduce a meter.
        }
        else if (args.PropertyName == nameof(DictationService.StatusText))
        {
            DeckStatusLabel.Text = _dictationService.StatusText.ToUpperInvariant();
        }
        else if (args.PropertyName == nameof(DictationService.ActiveModelName))
        {
            ActiveModelLabel.Text = ResolveActiveModelLabel();
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (AllowClose) return;
        e.Cancel = true;
        Hide();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // The old TC-80 shell hid to tray on Minimize. The new light shell
        // has a normal in-app titlebar, so Minimize should just minimize —
        // the user can always restore via the taskbar or the tray icon.
        // The tray icon still exists for status / settings / exit.

        // Persist the maximised flag separately from size, so the
        // RestoreWindowPlacement path can apply it on next launch.
        if (_settings != null && IsLoaded)
        {
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            SchedulePlacementSave();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _openSettingsAction();
            e.Handled = true;
        }
    }

    // ─── TITLEBAR WINDOW CONTROLS ─────────────────────────────────────────────

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Chromeless window has no OS titlebar to drag — wire the in-app
        // titlebar to drag the window instead. DragMove only works in
        // Normal state and only on a real left-button press.
        if (e.ChangedButton != MouseButton.Left) return;
        if (WindowState != WindowState.Normal) return;
        if (e.ButtonState != MouseButtonState.Pressed) return;
        try { DragMove(); }
        catch (InvalidOperationException) { /* ignore races */ }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Hides to tray (matches the legacy minimize-on-X behavior). The
        // tray icon's Exit option remains the proper way to terminate the
        // app, so we don't need to actually close the window here. This
        // also avoids the "ShowMainWindow can't re-show a closed window"
        // crash on the next tray-icon click.
        Hide();
    }

    // ─── SIDEBAR NAV ───────────────────────────────────────────────────────────

    private void ShowOnly(UIElement visible)
    {
        HomeView.Visibility     = visible == HomeView     ? Visibility.Visible : Visibility.Collapsed;
        DictationView.Visibility = visible == DictationView ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility   = visible == HistoryView   ? Visibility.Visible : Visibility.Collapsed;
        DictionaryView.Visibility = visible == DictionaryView ? Visibility.Visible : Visibility.Collapsed;
        StylesView.Visibility    = visible == StylesView    ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetActiveNav(ToggleButton active)
    {
        foreach (var btn in new[] { NavHomeBtn, NavDictationBtn, NavHistoryBtn, NavStylesBtn, NavTransformsBtn, NavSettingsBtn })
        {
            if (btn == null) continue;
            btn.IsChecked = btn == active;
        }
    }

    private void NavHomeBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavHomeBtn);
        ShowOnly(HomeView);
    }

    private void NavDictationBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavDictationBtn);
        ShowOnly(DictationView);
    }

    private void NavHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavHistoryBtn);
        ShowOnly(HistoryView);
        UpdateTabIndicators(activeIsHistory: true);
    }

    private void NavStylesBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavStylesBtn);
        ShowOnly(StylesView);
    }

    private void NavTransformsBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavTransformsBtn);
        // Transforms view = the dictionary & rules management (the "replace X
        // with Y when heard" corrections and vocabulary). The underlying
        // DictionaryService is unchanged; only the surface label changed.
        ShowOnly(DictionaryView);
        UpdateTabIndicators(activeIsHistory: false);
    }

    private void NavSettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        _openSettingsAction();
        // Keep the previously-selected nav highlighted (don't pretend Settings is a sub-page).
    }

    // ─── DICTATION (transports) ───────────────────────────────────────────────

    private void QuickDictationBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_dictationService.State == DictationService.DictationState.Idle)
        {
            _dictationService.StartManualRecording();
        }
        else
        {
            _dictationService.StopManualRecording();
        }
    }

    private void UpdateTransportState(DictationService.DictationState state)
    {
        switch (state)
        {
            case DictationService.DictationState.Recording:
                RecButtonLabel.Text = "● RECORDING";
                RecButtonLed.Fill = RecLedOn;
                RecordButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                _recordStartTime = DateTime.Now;
                _tapeTimer.Start();
                if (DictationViewState != null) DictationViewState.Text = "Listening…";
                break;

            case DictationService.DictationState.Transcribing:
                RecButtonLabel.Text = "● REC";
                RecButtonLed.Fill = RecLedOff;
                RecordButton.IsEnabled = false;
                StopButton.IsEnabled = false;
                _tapeTimer.Stop();
                if (DictationViewState != null) DictationViewState.Text = "Transcribing…";
                break;

            case DictationService.DictationState.Injecting:
                if (DictationViewState != null) DictationViewState.Text = "Injecting text…";
                break;

            case DictationService.DictationState.Idle:
                RecButtonLabel.Text = "● REC";
                RecButtonLed.Fill = RecLedOff;
                RecordButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                _tapeTimer.Stop();
                if (DictationViewState != null) DictationViewState.Text = "Idle — ready when you are";
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
        _dictationService.StartManualRecording();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _dictationService.StopManualRecording();
    }

    // ─── LEGACY TAB BUTTONS (kept so the in-view tab swap still works) ────────

    private void TabHistoryBtn_Click(object sender, RoutedEventArgs e)
    {
        // CLEAR LOG: actually delete all stored dictations (it used to just
        // navigate to the History view, so the button appeared to do nothing).
        if (_historyService.Entries.Count == 0) return;
        var result = MessageBox.Show(
            "Delete all stored dictations? This cannot be undone.",
            "Clear log",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            _historyService.ClearAll();
            Logger.Info("History cleared from Home > Clear log.");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to clear history", ex);
        }
    }

    private void TabDictionaryBtn_Click(object sender, RoutedEventArgs e)
    {
        SetActiveNav(NavTransformsBtn);
        ShowOnly(DictionaryView);
        UpdateTabIndicators(activeIsHistory: false);
    }

    private void UpdateTabIndicators(bool activeIsHistory)
    {
        // Indicators were always collapsed in the new light layout (no underline tab strip),
        // but the helpers still get called from the legacy click handlers — keep them safe.
    }

    // ─── HISTORY LOG ──────────────────────────────────────────────────────────

    private void RefreshHistoryList()
    {
        if (HistoryListBox == null || HistorySearchBox == null) return;

        var scroll = FindVisualChild<ScrollViewer>(HistoryListBox);
        string? topId = GetTopVisibleItemId<HistoryEntry>(HistoryListBox, scroll);

        string query = HistorySearchBox.Text;
        var items = _historyService.Search(query);

        HistoryListBox.ItemsSource = items;
        if (HistoryListBoxFull != null) HistoryListBoxFull.ItemsSource = items;

        // Toggle the "no dictations yet" empty state. Only show it on the
        // home view, and only when the user hasn't typed a search query
        // (an empty result for "hello" is a *real* empty result, not a
        // call-to-action).
        if (HistoryEmptyState != null)
        {
            HistoryEmptyState.Visibility =
                items.Count == 0 && string.IsNullOrWhiteSpace(query)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

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
        _historySearchTimer.Stop();
        _historySearchTimer.Start();
    }

    private void CopyTranscriptBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string text)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
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
        _dictSearchTimer.Stop();
        _dictSearchTimer.Start();
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
