using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;

namespace MadiAI;

/// <summary>
/// Types Unicode text into a user-selected visible app after explicit approval.
/// Never sends Enter, submits forms, clicks, or controls trading terminals.
/// </summary>
internal static class WindowsTextAutomation
{
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    public static async Task<string> TypeIntoAsync(string appName, string text)
    {
        appName = appName.Trim();
        if (appName.Length < 2 || appName.Length > 60 || text.Length < 1 || text.Length > 1000)
            return "Specify an app and up to 1,000 characters to type.";
        if (appName.Contains("mt5", StringComparison.OrdinalIgnoreCase) ||
            appName.Contains("metatrader", StringComparison.OrdinalIgnoreCase))
            return "Typing directly into trading terminals is disabled. Use the dedicated MT5 integration.";

        var matches = Process.GetProcesses().Where(p =>
        {
            try
            {
                return p.MainWindowHandle != IntPtr.Zero &&
                       p.ProcessName != "MadiAI" &&
                       (p.ProcessName.Contains(appName, StringComparison.OrdinalIgnoreCase) ||
                        p.MainWindowTitle.Contains(appName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }).Take(3).ToArray();
        if (matches.Length == 0) return "No open application matched " + appName + ".";
        if (matches.Length > 1) return "Several windows matched " + appName + ". Please use a more specific app name.";

        var target = matches[0];
        var preview = text.Length > 160 ? text[..160] + "…" : text;
        var answer = System.Windows.MessageBox.Show(
            "Type this text into " + target.ProcessName + "?\n\n" + preview +
            "\n\nThis will not press Enter or submit anything. Make sure the correct input field is selected.",
            "Madi AI • Confirm typing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return "Typing cancelled.";

        ShowWindow(target.MainWindowHandle, 9);
        SetForegroundWindow(target.MainWindowHandle);
        await Task.Delay(250);
        if (GetForegroundWindow() != target.MainWindowHandle)
            return "Windows did not allow me to focus the target app. No text was entered.";

        var events = new Input[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            events[i * 2] = new Input
            {
                Type = 1,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput { ScanCode = text[i], Flags = 0x0004 }
                }
            };
            events[i * 2 + 1] = new Input
            {
                Type = 1,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput { ScanCode = text[i], Flags = 0x0004 | 0x0002 }
                }
            };
        }
        var sent = SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
        return sent == events.Length
            ? "Typed the requested text into " + target.ProcessName + " without submitting it."
            : "Windows accepted only " + sent + " of " + events.Length + " keyboard events. Check the target app.";
    }
}
