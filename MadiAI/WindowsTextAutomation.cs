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

    // INPUT has a four-byte padding gap after type on Win64. The installer
    // targets win-x64; SendInput rejects structures with the wrong cbSize.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    internal static bool HasValidNativeLayout =>
        IntPtr.Size == 8 && Marshal.SizeOf<Input>() == 40 &&
        Marshal.SizeOf<KeyboardInput>() == 24;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    // Deliberately use WM_CLOSE via CloseMainWindow rather than killing processes.
    // Windows/app-owned unsaved-work prompts remain under the user's control.
    internal static string CloseVisibleApp(string appName)
    {
        if (string.IsNullOrWhiteSpace(appName) || appName.Length > 60)
            return "Please specify the visible application to close.";
        var matches = FindSafeVisibleWindows(appName);
        if (matches.Length == 0) return "No safe open application matched " + appName + ".";
        if (matches.Length > 1) return "Multiple windows matched. Specify a more precise application or window title.";
        var target = matches[0];
        if (System.Windows.MessageBox.Show("Request " + target.ProcessName + " to close?\n\n" +
            "Unsaved changes may cause the app to ask you for confirmation. Madi will not force quit it.",
            "Madi AI • Confirm closing app", MessageBoxButton.YesNo,
            MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return "Closing the application was cancelled.";
        try
        {
            return target.CloseMainWindow()
                ? "Asked " + target.ProcessName + " to close. Please respond to any unsaved-work prompts."
                : "The application did not accept a normal close request. It was not force-terminated.";
        }
        catch (Exception ex) { return "Unable to request closing the app: " + ex.Message; }
    }

    internal static string ChangeVisibleWindowState(string appName, bool minimize)
    {
        if (string.IsNullOrWhiteSpace(appName) || appName.Length > 60)
            return "Please specify the application.";
        var matches = FindSafeVisibleWindows(appName);
        if (matches.Length == 0) return "No safe open application matched " + appName + ".";
        if (matches.Length > 1) return "Several windows matched. Use a more specific name.";
        var target = matches[0];
        try
        {
            if (minimize) ShowWindow(target.MainWindowHandle, 6); // SW_MINIMIZE
            else ShowWindow(target.MainWindowHandle, 3); // SW_MAXIMIZE
            return (minimize ? "Minimized " : "Maximized ") + target.ProcessName + ".";
        }
        catch (Exception ex) { return "Unable to change the window state: " + ex.Message; }
    }

    internal static bool IsProtectedWindow(string processName, string title) =>
        processName.Equals("MadiAI", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("explorer", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("taskmgr", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("terminal64", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("terminal", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("MetaTrader", StringComparison.OrdinalIgnoreCase);

    private static Process[] FindSafeVisibleWindows(string appName) =>
        Process.GetProcesses().Where(p =>
        {
            try
            {
                var name = p.ProcessName;
                var title = p.MainWindowTitle;
                return p.MainWindowHandle != IntPtr.Zero &&
                    !IsProtectedWindow(name, title) &&
                    (name.Contains(appName, StringComparison.OrdinalIgnoreCase) ||
                     title.Contains(appName, StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }).Take(3).ToArray();

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
        if (IsProtectedWindow(target.ProcessName, target.MainWindowTitle))
            return "Typing into a trading terminal is disabled. Use the dedicated MT5 bridge.";
        if (!HasValidNativeLayout)
            return "Windows keyboard automation is unavailable for this platform.";
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
                Keyboard = new KeyboardInput { ScanCode = text[i], Flags = 0x0004 }
            };
            events[i * 2 + 1] = new Input
            {
                Type = 1,
                Keyboard = new KeyboardInput { ScanCode = text[i], Flags = 0x0004 | 0x0002 }
            };
        }
        var sent = SendInput((uint)events.Length, events, Marshal.SizeOf<Input>());
        return sent == events.Length
            ? "Typed the requested text into " + target.ProcessName + " without submitting it."
            : "Windows accepted only " + sent + " of " + events.Length + " keyboard events. Check the target app.";
    }
}
