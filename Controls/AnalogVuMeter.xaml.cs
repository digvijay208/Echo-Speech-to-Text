using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using UserControl = System.Windows.Controls.UserControl;
using Color = System.Windows.Media.Color;

namespace Echo.Controls;

/// <summary>
/// Simulates a 1980s analog VU meter with realistic ballistic needle inertia and peak LED latch.
/// </summary>
public partial class AnalogVuMeter : UserControl
{
    private const double MinAngle = -36.0; // Rest position (-20 dB)
    private const double MaxAngle = 36.0;  // Full deflection (+6 dB)

    private double _currentAngle = MinAngle;
    private double _targetAngle = MinAngle;
    private double _needleVelocity = 0;
    private int _peakHoldFrames = 0;

    private readonly DispatcherTimer _physicsTimer;

    private static readonly SolidColorBrush PeakOffBrush = new(Color.FromRgb(0x33, 0x00, 0x00));
    private static readonly SolidColorBrush PeakOnBrush = new(Color.FromRgb(0xE6, 0x39, 0x46));

    public AnalogVuMeter()
    {
        InitializeComponent();

        // 60fps needle ballistic animation timer. Started on demand only ? it used to run
        // for the whole life of the process, burning CPU 60x/sec while sitting in the tray.
        _physicsTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _physicsTimer.Tick += OnPhysicsTick;
    }

    /// <summary>
    /// Sets the raw normalized audio level (0.0 to 1.0 RMS).
    /// </summary>
    public void SetAudioLevel(float level)
    {
        // Convert normalized linear RMS to logarithmic dB scale for realistic needle swing
        // -20 dB to +3 dB mapping
        double norm = Math.Clamp(level * 4.0, 0.0, 1.0); // Boost speech RMS for visual meter response

        // S-curve dB response
        double dbFactor = Math.Pow(norm, 0.6); // Fast attack curve
        _targetAngle = MinAngle + (MaxAngle - MinAngle) * dbFactor;

        // Check peak overload
        if (norm > 0.85)
        {
            _peakHoldFrames = 12; // Hold peak LED on for ~200ms
        }

        _physicsTimer.Start();
    }

    private void OnPhysicsTick(object? sender, EventArgs e)
    {
        // Spring physics: Damped Harmonic Oscillator (Simulates physical needle movement)
        double stiffness = 0.28;
        double damping = 0.72;

        double force = (_targetAngle - _currentAngle) * stiffness;
        _needleVelocity = (_needleVelocity + force) * damping;
        _currentAngle += _needleVelocity;

        // Clamp to physical meter bezel boundaries
        if (_currentAngle < MinAngle - 2)
        {
            _currentAngle = MinAngle - 2;
            _needleVelocity = 0;
        }
        else if (_currentAngle > MaxAngle + 3)
        {
            _currentAngle = MaxAngle + 3;
            _needleVelocity = 0;
        }

        NeedleTransform.Angle = _currentAngle;

        // Decay target gently if no new audio signal
        _targetAngle = Math.Max(MinAngle, _targetAngle - 1.8);

        // Peak LED latch logic
        if (_peakHoldFrames > 0)
        {
            _peakHoldFrames--;
            PeakLed.Fill = PeakOnBrush;
        }
        else
        {
            PeakLed.Fill = PeakOffBrush;
        }

        // Needle has settled at rest and the peak LED is off ? nothing left to animate.
        if (_peakHoldFrames == 0 &&
            _targetAngle <= MinAngle &&
            Math.Abs(_currentAngle - MinAngle) < 0.05 &&
            Math.Abs(_needleVelocity) < 0.05)
        {
            _currentAngle = MinAngle;
            _needleVelocity = 0;
            NeedleTransform.Angle = MinAngle;
            _physicsTimer.Stop();
        }
    }

    public void Reset()
    {
        _targetAngle = MinAngle;
        _peakHoldFrames = 0;
        // Keep ticking so the needle falls back smoothly instead of freezing mid-swing.
        _physicsTimer.Start();
    }
}
