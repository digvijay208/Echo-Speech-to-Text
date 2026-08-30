using System.Runtime.InteropServices;
using System.Windows;
using Echo.Native;

namespace Echo.Services;

/// <summary>
/// Injects text into the currently focused application.
/// Primary method: Win32 SendInput (Unicode keystrokes).
/// Fallback method: Clipboard copy + Ctrl+V paste.
/// </summary>
public static class TextInjectionService
{
    /// <summary>
    /// Injects text into the focused window.
    /// </summary>
    public static void InjectText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        Logger.Info($"Attempting to inject text ({text.Length} chars): \"{text}\"");

        // SendInput is a blocking native call and can take a long time for a chatty
        // target (Chrome, Electron editors, etc.). Running it on the UI thread freezes
        // the entire app ? including the HUD overlay ? for the full injection duration.
        // Offload to a worker so the UI stays responsive even if the target is sluggish.
        Task.Run(() =>
        {
            try
            {
                bool success = TrySendInputUnicode(text);

                if (!success)
                {
                    Logger.Warn("SendInput failed or was rejected; falling back to Clipboard + Ctrl+V paste.");
                    TryClipboardPaste(text);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Text injection failed", ex);
            }
        });
    }

    private static bool TrySendInputUnicode(string text)
    {
        try
        {
            int structSize = Marshal.SizeOf<Win32.INPUT>();
            int totalTyped = 0;

            // Build every keystroke in a single INPUT[] and submit as one SendInput call.
            // Splitting into 128-char chunks meant dozens of round-trips through the OS input
            // queue per utterance; coalescing into a single batch is materially faster on
            // chatty targets (Chrome, VSCode, Electron editors) because the OS only schedules
            // one input-processor wake-up instead of one per chunk. A single call also keeps
            // the keystrokes contiguous from the target's perspective.
            // We still split if the array would exceed the SendInput limit (4096 elements
            // is the documented soft cap, but we stay well under with a 512 char ceiling
            // on chars per call ? 1024 INPUTs, since each char is down+up).
            const int CharsPerBatch = 512;

            for (int offset = 0; offset < text.Length; )
            {
                int count = Math.Min(CharsPerBatch, text.Length - offset);

                // Never split a surrogate pair across two SendInput calls ? the high
                // surrogate arriving without its low half turns into a replacement char.
                while (count < text.Length - offset &&
                       char.IsHighSurrogate(text[offset + count - 1]))
                {
                    count++;
                }

                var inputArray = BuildUnicodeInputs(text, offset, count);
                uint sent = Win32.SendInput((uint)inputArray.Length, inputArray, structSize);

                if (sent != inputArray.Length)
                {
                    int error = Marshal.GetLastWin32Error();
                    Logger.Warn($"SendInput: only {sent}/{inputArray.Length} inputs processed at offset {offset}. Win32 error: {error}, StructSize: {structSize}");

                    // Nothing landed at all ? safe to fall back to the clipboard.
                    if (totalTyped == 0 && sent == 0)
                        return false;

                    // Partially typed. Falling back now would paste the whole string again
                    // on top of what is already there, so stop and report success.
                    Logger.Error($"SendInput truncated after {totalTyped} characters; not retrying to avoid duplicated text.");
                    return true;
                }

                totalTyped += count;
                offset += count;
            }

            Logger.Info($"Successfully injected {totalTyped} characters via SendInput.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Exception in SendInput", ex);
            return false;
        }
    }

    private static Win32.INPUT[] BuildUnicodeInputs(string text, int offset, int count)
    {
        var inputs = new Win32.INPUT[count * 2];

        for (int i = 0; i < count; i++)
        {
            ushort scan = text[offset + i];

            // Key down
            inputs[i * 2] = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.INPUTUNION
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scan,
                        dwFlags = Win32.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            // Key up
            inputs[i * 2 + 1] = new Win32.INPUT
            {
                type = Win32.INPUT_KEYBOARD,
                u = new Win32.INPUTUNION
                {
                    ki = new Win32.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scan,
                        dwFlags = Win32.KEYEVENTF_UNICODE | Win32.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        return inputs;
    }

    private static void TryClipboardPaste(string text)
    {
        try
        {
            // Set clipboard on STA thread
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.Clipboard.SetDataObject(text, true);
            });

            // Small pause for clipboard to be available
            Thread.Sleep(50);

            // Send Ctrl+V keystroke
            var pasteInputs = new Win32.INPUT[]
            {
                // Ctrl down
                new() { type = Win32.INPUT_KEYBOARD, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT { wVk = (ushort)Win32.VK_CONTROL, dwFlags = 0 } } },
                // V down
                new() { type = Win32.INPUT_KEYBOARD, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT { wVk = (ushort)Win32.VK_V, dwFlags = 0 } } },
                // V up
                new() { type = Win32.INPUT_KEYBOARD, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT { wVk = (ushort)Win32.VK_V, dwFlags = Win32.KEYEVENTF_KEYUP } } },
                // Ctrl up
                new() { type = Win32.INPUT_KEYBOARD, u = new Win32.INPUTUNION { ki = new Win32.KEYBDINPUT { wVk = (ushort)Win32.VK_CONTROL, dwFlags = Win32.KEYEVENTF_KEYUP } } }
            };

            uint sent = Win32.SendInput((uint)pasteInputs.Length, pasteInputs, Marshal.SizeOf<Win32.INPUT>());
            Logger.Info($"Clipboard paste keystrokes sent: {sent}/{pasteInputs.Length}");
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to inject text via clipboard paste", ex);
        }
    }
}
