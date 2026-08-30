using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Echo.Native;
using Echo.Services;

namespace Echo.Views;

/// <summary>
/// Non-activating floating HUD overlay that displays recording state,
/// audio levels, and transcript preview. Never steals focus from the target app.
/// </summary>
public partial class HudOverlayWindow : Window
{
    private Storyboard? _pulseStoryboard;
    private Storyboard? _fadeInStoryboard;
    private readonly Storyboard _spinnerStoryboard;

    public HudOverlayWindow()
    {
        InitializeComponent();

        // Resolve up front. These used to be fetched in Window_Loaded, which does not run
        // until the first Show() ? so the very first ShowHud() animated nothing.
        _pulseStoryboard = (Storyboard)FindResource("PulseAnimation");
        _fadeInStoryboard = (Storyboard)FindResource("FadeIn");

        // Create spinner animation programmatically
        _spinnerStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(spinAnim, SpinnerIndicator);
        Storyboard.SetTargetProperty(spinAnim,
            new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinnerStoryboard.Children.Add(spinAnim);
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
        // Position at bottom center of primary screen
        PositionAtBottomCenter();
    }

    /// <summary>
    /// Begins a storyboard against this window's name scope.
    /// Storyboards that use Storyboard.TargetName cannot be started with the parameterless
    /// Begin() ? WPF throws "No applicable name scope exists to resolve the name ...".
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

    /// <summary>
    /// Shows the HUD with a fade-in animation.
    /// </summary>
    public void ShowHud()
    {
        PositionAtBottomCenter();
        Show();
        PositionAtBottomCenter();
        SetRecordingState();
        BeginScoped(_fadeInStoryboard);
    }

    /// <summary>
    /// Hides the HUD with a fade-out.
    /// </summary>
    public void HideHud()
    {
        StopScoped(_pulseStoryboard);
        StopScoped(_fadeInStoryboard);
        StopScoped(_spinnerStoryboard);
        HudContainer.Opacity = 0;
        HudTranslate.Y = 20;
        Hide();
    }

    /// <summary>
    /// Updates the HUD to show recording state.
    /// </summary>
    public void SetRecordingState()
    {
        RecordingDot.Visibility = Visibility.Visible;
        RecordingGlow.Visibility = Visibility.Visible;
        SpinnerIndicator.Visibility = Visibility.Collapsed;
        DoneCheck.Visibility = Visibility.Collapsed;
        AudioBars.Visibility = Visibility.Visible;

        StatusLabel.Text = "Listening...";
        TranscriptLabel.Visibility = Visibility.Collapsed;

        StopScoped(_spinnerStoryboard);
        BeginScoped(_pulseStoryboard);
    }

    /// <summary>
    /// Updates the HUD to show transcribing state.
    /// </summary>
    public void SetTranscribingState()
    {
        StopScoped(_pulseStoryboard);
        RecordingDot.Visibility = Visibility.Collapsed;
        RecordingGlow.Visibility = Visibility.Collapsed;
        SpinnerIndicator.Visibility = Visibility.Visible;
        DoneCheck.Visibility = Visibility.Collapsed;
        AudioBars.Visibility = Visibility.Collapsed;

        StatusLabel.Text = "Transcribing...";
        TranscriptLabel.Visibility = Visibility.Collapsed;

        BeginScoped(_spinnerStoryboard);
    }

    /// <summary>
    /// Updates the HUD to show completion with the transcript.
    /// </summary>
    public void SetDoneState(string transcript)
    {
        StopScoped(_pulseStoryboard);
        StopScoped(_spinnerStoryboard);

        RecordingDot.Visibility = Visibility.Collapsed;
        RecordingGlow.Visibility = Visibility.Collapsed;
        SpinnerIndicator.Visibility = Visibility.Collapsed;
        DoneCheck.Visibility = Visibility.Visible;
        AudioBars.Visibility = Visibility.Collapsed;

        StatusLabel.Text = "Done!";
        TranscriptLabel.Text = transcript;
        TranscriptLabel.Visibility = string.IsNullOrEmpty(transcript)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Updates the audio level bars based on RMS value (0.0 - 1.0).
    /// </summary>
    public void UpdateAudioLevel(float level)
    {
        // Scale level for visual effect (audio RMS is usually quite low)
        float scaled = Math.Min(1.0f, level * 5.0f);

        double baseHeight = 6;
        double maxExtra = 24;

        Bar1.Height = baseHeight + maxExtra * scaled * 0.4;
        Bar2.Height = baseHeight + maxExtra * scaled * 0.7;
        Bar3.Height = baseHeight + maxExtra * scaled * 1.0;
        Bar4.Height = baseHeight + maxExtra * scaled * 0.7;
        Bar5.Height = baseHeight + maxExtra * scaled * 0.4;
    }

    private void PositionAtBottomCenter()
    {
        var screen = SystemParameters.WorkArea;
        Left = (screen.Width - Width) / 2;
        Top = screen.Bottom - Height - 60;
    }
}
