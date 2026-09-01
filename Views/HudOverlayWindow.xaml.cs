using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Echo.Native;
using Echo.Services;
using Rectangle = System.Windows.Shapes.Rectangle;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;

namespace Echo.Views;

/// <summary>
/// Non-activating floating "Echo Bar" overlay with five visual states:
/// Idle (mic + hint), Listening (pulsing dot, waveform, timer, cancel),
/// Processing (spinner, purple wave, "Cleaning up…"), Inserted (green
/// check, transcript, Undo) and Error (alert, retry). Never steals focus
/// from the target app.
/// </summary>
public partial class HudOverlayWindow : Window
{
    private enum HudVisual { Idle, Listening, Processing, Inserted, Error }

    private const int BarCount = 22;
    private const double BarMinHeight = 3.5;
    private const double BarMaxExtra = 26;

    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly double[] _barHeights = new double[BarCount];
    private readonly double[] _envelope = new double[BarCount];

    private readonly Brush _waveBlue = new SolidColorBrush(Color.FromRgb(0x4E, 0x9B, 0xFF));
    private readonly Brush _wavePurple = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));

    private Storyboard? _pulseStoryboard;
    private Storyboard? _fadeInStoryboard;
    private readonly Storyboard _spinnerStoryboard;

    private readonly DispatcherTimer _animTimer;   // waveform + elapsed clock (~30 fps)
    private readonly DispatcherTimer _errorTimer;  // auto-dismisses the error card

    private HudVisual _visual = HudVisual.Idle;
    private float _audioLevel;
    private float _levelSmoothed;
    private double _phase;
    private DateTime _listenStart;
    private int _undoChars;

    /// <summary>Raised when the user clicks Undo: arg = injected char count to delete.</summary>
    public event Action<int>? UndoRequested;

    /// <summary>Raised when the user clicks "Try again" on the error card.</summary>
    public event Action? RetryRequested;

    /// <summary>Raised when the user clicks ✕ while listening.</summary>
    public event Action? CancelRequested;

    public HudOverlayWindow()
    {
        InitializeComponent();

        // Resolve up front. These used to be fetched in Window_Loaded, which does not run
        // until the first Show() — so the very first ShowHud() animated nothing.
        _pulseStoryboard = (Storyboard)FindResource("PulseAnimation");
        _fadeInStoryboard = (Storyboard)FindResource("FadeIn");

        // Spinner rotation, built programmatically (targets a named transform).
        _spinnerStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(spinAnim, SpinnerVisual);
        Storyboard.SetTargetProperty(spinAnim,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinnerStoryboard.Children.Add(spinAnim);

        BuildWaveform();

        _animTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _animTimer.Tick += AnimTick;

        _errorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _errorTimer.Tick += (_, _) =>
        {
            _errorTimer.Stop();
            HideHud();
        };

        // Safe defaults; the first state change repaints immediately.
        SetVisual(HudVisual.Idle);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Apply WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW before the window is ever shown,
        // otherwise the first Show() can still steal focus from the target app.
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int exStyle = Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE,
                exStyle | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW);
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottomCenter();
    }

    /// <summary>
    /// Begins a storyboard against this window's name scope.
    /// Storyboards that use Storyboard.TargetName cannot be started with the parameterless
    /// Begin() — WPF throws "No applicable name scope exists to resolve the name ...".
    /// </summary>
    private void BeginScoped(Storyboard? storyboard)
    {
        if (storyboard == null) return;
        try
        {
            storyboard.Begin(this, isControllable: true);
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to start HUD storyboard", ex);
        }
    }

    private void StopScoped(Storyboard? storyboard)
    {
        if (storyboard == null) return;
        try
        {
            storyboard.Stop(this);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to stop HUD storyboard: {ex.Message}");
        }
    }

    // ========================================================================
    // Public state API (called from App.xaml.cs)
    // ========================================================================

    /// <summary>Shows the bar with a fade-in. Does not force a state — the
    /// DictationService state change that triggered the show sets the visual.</summary>
    public void ShowHud()
    {
        PositionAtBottomCenter();
        Show();
        PositionAtBottomCenter();
        _errorTimer.Stop();
        if (!_animTimer.IsEnabled) _animTimer.Start();
        BeginScoped(_fadeInStoryboard);
    }

    /// <summary>Hides the bar.</summary>
    public void HideHud()
    {
        _animTimer.Stop();
        _errorTimer.Stop();
        StopScoped(_pulseStoryboard);
        StopScoped(_fadeInStoryboard);
        StopScoped(_spinnerStoryboard);
        HudContainer.Opacity = 0;
        HudTranslate.Y = 18;
        Hide();
    }

    /// <summary>Idle: mic icon + "Echo · Hold Right Ctrl to speak".</summary>
    public void SetIdleState()
    {
        TranscriptRow.Visibility = Visibility.Collapsed;
        SetVisual(HudVisual.Idle);
    }

    /// <summary>Listening: pulsing red dot, blue waveform, elapsed timer, ✕ cancel.</summary>
    public void SetRecordingState()
    {
        _listenStart = DateTime.Now;
        _audioLevel = 0;
        _levelSmoothed = 0;
        TranscriptRow.Visibility = Visibility.Collapsed;
        SetVisual(HudVisual.Listening);
    }

    /// <summary>Processing: purple spinner + wave, "Cleaning up…".</summary>
    public void SetTranscribingState()
    {
        TranscriptRow.Visibility = Visibility.Collapsed;
        SetVisual(HudVisual.Processing);
    }

    /// <summary>Inserted: green check, "Inserted", transcript, Undo.
    /// <paramref name="undoChars"/> = number of characters Undo should delete.</summary>
    public void SetDoneState(string transcript, int undoChars)
    {
        _undoChars = undoChars;
        CenterLabel.Text = "Inserted";

        if (string.IsNullOrWhiteSpace(transcript))
        {
            TranscriptRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            TranscriptText.Text = transcript;
            TranscriptRow.Visibility = Visibility.Visible;
        }

        SetActionButton(MakeLabel("Undo"), "undo");
        SetVisual(HudVisual.Inserted);
    }

    /// <summary>Error: red alert, message, "Try again". Auto-dismisses after 7 s.</summary>
    public void SetErrorState(string message)
    {
        CenterLabel.Text = string.IsNullOrWhiteSpace(message)
            ? "Couldn't hear you clearly."
            : message;
        TranscriptRow.Visibility = Visibility.Collapsed;

        SetActionButton(MakeLabel("Try again"), "retry");
        SetVisual(HudVisual.Error);

        _errorTimer.Stop();
        _errorTimer.Start();
    }

    /// <summary>Feeds the live mic level (0-1) into the waveform.</summary>
    public void UpdateAudioLevel(float level)
    {
        _audioLevel = level;
    }

    // ========================================================================
    // Internals
    // ========================================================================

    private void BuildWaveform()
    {
        for (int i = 0; i < BarCount; i++)
        {
            // Symmetric hump so the middle bars swing widest, like the mockup.
            _envelope[i] = Math.Pow(Math.Sin(Math.PI * (i + 0.5) / BarCount), 0.7);
            _barHeights[i] = BarMinHeight;

            var bar = new Rectangle
            {
                Width = 3,
                Height = BarMinHeight,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = _waveBlue,
                Margin = new Thickness(0, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _bars[i] = bar;
            WavePanel.Children.Add(bar);
        }
    }

    private void SetVisual(HudVisual v)
    {
        _visual = v;

        // Idle pill runs smaller and tighter than the active cards.
        bool idle = v == HudVisual.Idle;
        HudCard.Padding = idle ? new Thickness(12, 7, 10, 7) : new Thickness(16, 12, 16, 12);
        IdleVisual.Width = idle ? 24 : 32;
        IdleVisual.Height = idle ? 24 : 32;
        IdleVisual.CornerRadius = new CornerRadius(idle ? 12 : 16);
        MicGlyph.FontSize = idle ? 12 : 15;
        EchoTitle.FontSize = idle ? 12 : 14;
        IdleHint.FontSize = idle ? 11 : 12.5;

        IdleVisual.Visibility = v == HudVisual.Idle ? Visibility.Visible : Visibility.Collapsed;
        RecDotVisual.Visibility = v == HudVisual.Listening ? Visibility.Visible : Visibility.Collapsed;
        SpinnerVisual.Visibility = v == HudVisual.Processing ? Visibility.Visible : Visibility.Collapsed;
        CheckVisual.Visibility = v == HudVisual.Inserted ? Visibility.Visible : Visibility.Collapsed;
        WarnVisual.Visibility = v == HudVisual.Error ? Visibility.Visible : Visibility.Collapsed;

        IdleTextPanel.Visibility = v == HudVisual.Idle ? Visibility.Visible : Visibility.Collapsed;
        ActionButton.Visibility = v is HudVisual.Listening or HudVisual.Inserted or HudVisual.Error or HudVisual.Idle
            ? Visibility.Visible
            : Visibility.Collapsed;
        CenterLabel.Visibility = v is HudVisual.Inserted or HudVisual.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        WavePanel.Visibility = v is HudVisual.Listening or HudVisual.Processing
            ? Visibility.Visible
            : Visibility.Collapsed;
        RightText.Visibility = v is HudVisual.Listening or HudVisual.Processing
            ? Visibility.Visible
            : Visibility.Collapsed;

        switch (v)
        {
            case HudVisual.Listening:
                for (int i = 0; i < BarCount; i++) _bars[i].Fill = _waveBlue;
                RightText.Text = "0:00";
                SetActionButton(MakeGlyph("\uE711", 10), "cancel");
                StopScoped(_spinnerStoryboard);
                BeginScoped(_pulseStoryboard);
                break;

            case HudVisual.Processing:
                for (int i = 0; i < BarCount; i++) _bars[i].Fill = _wavePurple;
                RightText.Text = "Cleaning up…";
                StopScoped(_pulseStoryboard);
                BeginScoped(_spinnerStoryboard);
                break;

            case HudVisual.Inserted:
                SetActionButton(MakeLabel("Undo"), "undo");
                StopScoped(_pulseStoryboard);
                StopScoped(_spinnerStoryboard);
                break;

            case HudVisual.Error:
                SetActionButton(MakeLabel("Try again"), "retry");
                StopScoped(_pulseStoryboard);
                StopScoped(_spinnerStoryboard);
                break;

            case HudVisual.Idle:
                SetActionButton(MakeGlyph("\uE711", 9), "dismiss");
                StopScoped(_pulseStoryboard);
                StopScoped(_spinnerStoryboard);
                break;
        }
    }

    private void SetActionButton(UIElement content, string kind)
    {
        ActionButton.Content = content;
        ActionButton.Tag = kind;
    }

    private static UIElement MakeLabel(string text)
    {
        return new TextBlock { Text = text };
    }

    private static UIElement MakeGlyph(string glyph, double size)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = size,
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCD, 0xD8))
        };
    }

    private void AnimTick(object? sender, EventArgs e)
    {
        _phase += 0.45;

        if (_visual == HudVisual.Listening)
        {
            // Smooth the raw level, then drive the bars: envelope shapes the
            // hump, wobble adds per-bar life so it reads as a waveform.
            _levelSmoothed = _levelSmoothed * 0.72f + _audioLevel * 0.28f;
            float lvl = Math.Clamp(_levelSmoothed * 5f, 0f, 1f);

            for (int i = 0; i < BarCount; i++)
            {
                double wobble = 0.55 + 0.45 * Math.Sin(_phase * 2.1 + i * 0.55);
                double target = BarMinHeight + BarMaxExtra * _envelope[i] * (0.12 + 0.88 * lvl) * wobble;
                _barHeights[i] += (target - _barHeights[i]) * 0.4;
                _bars[i].Height = _barHeights[i];
            }

            int secs = (int)(DateTime.Now - _listenStart).TotalSeconds;
            RightText.Text = $"{secs / 60}:{secs % 60:D2}";
        }
        else if (_visual == HudVisual.Processing)
        {
            // No audio flows while transcribing — animate a synthetic wave instead.
            for (int i = 0; i < BarCount; i++)
            {
                double env = 0.4 + 0.6 * _envelope[i];
                double target = BarMinHeight + 22 * env * (0.5 + 0.5 * Math.Sin(_phase * 2.3 - i * 0.5));
                _barHeights[i] += (target - _barHeights[i]) * 0.35;
                _bars[i].Height = _barHeights[i];
            }
        }
        else
        {
            for (int i = 0; i < BarCount; i++)
            {
                _barHeights[i] += (BarMinHeight - _barHeights[i]) * 0.3;
                _bars[i].Height = _barHeights[i];
            }
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        switch (ActionButton.Tag as string)
        {
            case "cancel":
                Logger.Info("HUD: cancel requested.");
                CancelRequested?.Invoke();
                break;

            case "undo":
                ActionButton.IsEnabled = false;
                UndoRequested?.Invoke(_undoChars);
                HideHud();
                break;

            case "retry":
                _errorTimer.Stop();
                RetryRequested?.Invoke();
                break;

            case "dismiss":
                HideHud();
                break;
        }
    }

    private void PositionAtBottomCenter()
    {
        // Hug the taskbar: card bottom (12px margin inside the window) ends up
        // ~20px above the taskbar, like the reference screenshot.
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = screen.Bottom - Height - 8;
    }
}
