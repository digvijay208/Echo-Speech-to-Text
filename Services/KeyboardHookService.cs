using System.Diagnostics;
using Echo.Native;

namespace Echo.Services;

/// <summary>
/// Monitors a global low-level keyboard hook for push-to-talk key presses.
/// The key is observed (never swallowed) ? it passes through to the target app.
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly Win32.LowLevelKeyboardProc _hookCallback;
    private int _targetVkCode;
    private bool _isKeyDown;

    public event Action? PushToTalkPressed;
    public event Action? PushToTalkReleased;

    /// <summary>
    /// The virtual key code being monitored. Default: Right Ctrl (0xA3).
    /// </summary>
    public int TargetVkCode
    {
        get => _targetVkCode;
        set
        {
            if (_targetVkCode == value) return;
            _targetVkCode = value;

            // If the old key was still held, its key-up will now be ignored and dictation
            // would stay stuck in Recording forever. Release it here.
            if (_isKeyDown)
            {
                _isKeyDown = false;
                PushToTalkReleased?.Invoke();
            }
        }
    }

    public bool IsHooked => _hookId != IntPtr.Zero;

    public KeyboardHookService()
    {
        _targetVkCode = Win32.VK_RCONTROL;
        // Must hold a reference to the delegate to prevent GC collection
        _hookCallback = HookCallback;
    }

    /// <summary>
    /// Installs the global low-level keyboard hook.
    /// Must be called from the UI thread (needs a message pump).
    /// </summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = Win32.SetWindowsHookEx(
            Win32.WH_KEYBOARD_LL,
            _hookCallback,
            Win32.GetModuleHandle(module.ModuleName),
            0);

        if (_hookId == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to install keyboard hook. Error: {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }
    }

    /// <summary>
    /// Removes the keyboard hook.
    /// </summary>
    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _isKeyDown = false;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // An exception escaping a hook procedure crosses a native frame ? the process dies
        // and Windows drops the hook. Swallow and log instead.
        try
        {
            if (nCode >= 0)
            {
                var hookStruct = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();

                if (hookStruct.vkCode == (uint)_targetVkCode)
                {
                    if ((msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN) && !_isKeyDown)
                    {
                        _isKeyDown = true;
                        PushToTalkPressed?.Invoke();
                    }
                    else if ((msg == Win32.WM_KEYUP || msg == Win32.WM_SYSKEYUP) && _isKeyDown)
                    {
                        _isKeyDown = false;
                        PushToTalkReleased?.Invoke();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Keyboard hook callback failed", ex);
        }

        // ALWAYS pass the key through ? never swallow it
        return Win32.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }
}
