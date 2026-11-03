using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.Wave;
using Whisper.net;

namespace MadiAI;
public partial class MainWindow : Window
{
    private readonly SpeechSynthesizer speaker = new();
    private readonly DispatcherTimer stats = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer voiceChunk = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MadiAI");
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private string AuthFile => Path.Combine(profile, "unlock.json");
    private string ModelFile
    {
        get
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "models", "ggml-tiny.bin");
            return File.Exists(bundled) && new FileInfo(bundled).Length > 20_000_000
                ? bundled
                : Path.Combine(profile, "models", "ggml-tiny.bin");
        }
    }
    private WaveInEvent? microphone;
    private WaveFileWriter? waveWriter;
    private string? currentRecording;
    private bool listening;
    private bool unlocked;
    private bool processingAudio;
    private bool voicePreparing;
    private CancellationTokenSource? voiceSetupCancellation;
    private long oldIdle, oldKernel, oldUser;

    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(1440, SystemParameters.WorkArea.Width - 24));
        Height = Math.Max(MinHeight, Math.Min(850, SystemParameters.WorkArea.Height - 24));
        Directory.CreateDirectory(profile);
        GatePrompt.Text = File.Exists(AuthFile) ? "Tell me your secret code to open me" : "Create your secret code to open me";
        GateHelp.Text = File.Exists(AuthFile) ? "Enter your saved code. It remains stored on this PC." : "Set your secret code once. It will be kept on this PC through app updates.";
        Loaded += (_, _) => { Animate(); Speak("Tell me your secret code to open me"); SecretBox.Focus(); };
        stats.Tick += (_, _) => UpdateStats();
        voiceChunk.Tick += (_, _) => { voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } };
        stats.Start();
        UpdateStats();
        Closed += (_, _) => { voiceSetupCancellation?.Cancel(); listening = false; voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } speaker.Dispose(); http.Dispose(); };
    }

    private void Animate()
    {
        Spin(RingOne, 22); Spin(RingTwo, -17); Spin(RingThree, 12);
    }
    private static void Spin(System.Windows.Media.RotateTransform ring, int seconds)
    {
        var animation = new DoubleAnimation(0, seconds < 0 ? -360 : 360, TimeSpan.FromSeconds(Math.Abs(seconds)))
        { RepeatBehavior = RepeatBehavior.Forever };
        ring.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animation);
    }
    private void Speak(string text)
    {
        try
        {
            speaker.SpeakAsyncCancelAll();
            // Windows may not have Urdu/Hindi voices. Prefer an installed matching voice.
            var lang = LanguagePicker.SelectedIndex;
            var target = lang == 2 ? "ur" : lang == 3 ? "hi" : "en";
            var voice = speaker.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == target);
            if (voice != null) speaker.SelectVoice(voice.VoiceInfo.Name);
            speaker.SpeakAsync(text);
        }
        catch { /* Missing Windows voice is nonfatal; visual status still works. */ }
    }
    private void Respond(string message, bool speak = true)
    {
        StatusText.Text = message;
        if (speak) Speak(message);
    }

    private void Unlock()
    {
        var code = SecretBox.Password;
        if (!File.Exists(AuthFile) && code.Length < 6) { GateHelp.Text = "Use at least 6 characters for a new code."; return; }
        if (File.Exists(AuthFile) && code.Length == 0) { GateHelp.Text = "Enter your saved secret code."; return; }
        try
        {
            if (File.Exists(AuthFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AuthFile));
                var salt = Convert.FromBase64String(doc.RootElement.GetProperty("salt").GetString()!);
                var saved = Convert.FromBase64String(doc.RootElement.GetProperty("hash").GetString()!);
                var entered = Rfc2898DeriveBytes.Pbkdf2(code, salt, 200000, HashAlgorithmName.SHA256, 32);
                if (!CryptographicOperations.FixedTimeEquals(saved, entered))
                { GateHelp.Text = "Incorrect secret code. Try again."; return; }
            }
            else
            {
                var salt = RandomNumberGenerator.GetBytes(16);
                var hash = Rfc2898DeriveBytes.Pbkdf2(code, salt, 200000, HashAlgorithmName.SHA256, 32);
                var json = JsonSerializer.Serialize(new { salt = Convert.ToBase64String(salt), hash = Convert.ToBase64String(hash) });
                File.WriteAllText(AuthFile, json);
            }
            unlocked = true;
            SecretBox.Clear();
            GateOverlay.Visibility = Visibility.Collapsed;
            CoreState.Text = "MADI AI • ONLINE";
            Respond("Welcome, friend. Madi AI is online and ready for your commands.");
        }
        catch (Exception ex) { GateHelp.Text = "Unable to open profile: " + ex.Message; }
    }
    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();
    private void SecretBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) Unlock(); }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void CommandBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) Execute(CommandBox.Text); }
    private void Send_Click(object sender, RoutedEventArgs e) => Execute(CommandBox.Text);

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    private static string Normalize(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        foreach (var prefix in new[] { "hey madi ai ", "hey madi ", "madi ai ", "madi ", "please ", "یار ", "براہ کرم ", "कृपया " })
            if (s.StartsWith(prefix)) s = s[prefix.Length..];
        return s;
    }
    private void Execute(string raw)
    {
        if (!unlocked || string.IsNullOrWhiteSpace(raw)) return;
        var text = Normalize(raw);
        CommandBox.Text = "";
        try
        {
            // Route searches before generic website launchers; a search request
            // must not silently open the homepage instead.
            if (text is "volume up" or "increase volume" or "آواز بڑھاؤ" or "आवाज़ बढ़ाओ")
            { PressMediaKey(0xAF); Respond("Volume increased.", false); return; }
            if (text is "volume down" or "decrease volume" or "آواز کم کرو" or "आवाज़ कम करो")
            { PressMediaKey(0xAE); Respond("Volume decreased.", false); return; }
            if (text is "mute" or "unmute" or "mute volume" or "خاموش" or "म्यूट")
            { PressMediaKey(0xAD); Respond("Toggled system mute.", false); return; }
            if (text is "play pause" or "pause music" or "resume music")
            { PressMediaKey(0xB3); Respond("Toggled media playback.", false); return; }
            if (text is "lock pc" or "lock computer" or "lock windows")
            { LockWorkStation(); return; }
            if (text is "open task manager" or "task manager")
            { Open("taskmgr.exe"); Respond("Opening Task Manager."); return; }
            if (text is "open downloads" or "downloads folder")
            { Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")); Respond("Opening Downloads."); return; }
            if (text is "open documents" or "documents folder")
            { Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); Respond("Opening Documents."); return; }
            if (text is "open pictures" or "pictures folder")
            { Open(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); Respond("Opening Pictures."); return; }
            if (text.StartsWith("close app "))
            { CloseInstalledApp(text[10..].Trim()); return; }
            if (text.StartsWith("search youtube ") || text.StartsWith("youtube search "))
            {
                var query = text.StartsWith("search youtube ") ? text[15..] : text[15..];
                Open("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query.Trim()));
                Respond("Searching YouTube for " + query.Trim()); return;
            }
            if (text.StartsWith("search ebay ") || text.StartsWith("ebay search "))
            {
                var query = text.StartsWith("search ebay ") ? text[12..] : text[12..];
                Open("https://www.ebay.com/sch/i.html?_nkw=" + Uri.EscapeDataString(query.Trim()));
                Respond("Searching eBay for " + query.Trim()); return;
            }
            if (text.StartsWith("search web ") || text.StartsWith("search google "))
            {
                var query = text.StartsWith("search web ") ? text[11..] : text[14..];
                Open("https://www.google.com/search?q=" + Uri.EscapeDataString(query.Trim()));
                Respond("Searching the web for " + query.Trim()); return;
            }
            if (text.StartsWith("open app ") || text.StartsWith("launch app ") || text.StartsWith("start app "))
            {
                LaunchInstalledApp(text[(text.IndexOf("app ", StringComparison.Ordinal) + 4)..].Trim()); return;
            }
            if (text.StartsWith("launch ") || text.StartsWith("start application "))
            {
                LaunchInstalledApp(text.StartsWith("launch ") ? text[7..].Trim() : text[18..].Trim()); return;
            }
            if (text is "list apps" or "installed apps" or "show apps")
            {
                ShowAppPicker(); return;
            }
            if (text.Contains("youtube") || text.Contains("یوٹیوب") || text.Contains("यूट्यूब")) { Open("https://www.youtube.com"); Respond("Opening YouTube."); return; }
            if (text.Contains("whatsapp") || text.Contains("واٹس ایپ") || text.Contains("व्हाट्सएप")) { Open("https://web.whatsapp.com"); Respond("Opening WhatsApp Web. Personal WhatsApp call control is not supported."); return; }
            if (text.Contains("aliexpress") || text.Contains("علی ایکسپریس") || text.Contains("अलीएक्सप्रेस")) { Open("https://www.aliexpress.com"); Respond("Opening AliExpress."); return; }
            if (text.Contains("ebay") || text.Contains("ای بے") || text.Contains("ईबे")) { Open("https://www.ebay.com"); Respond("Opening eBay."); return; }
            if (text.Contains("canva") || text.Contains("کینوا") || text.Contains("कैनवा")) { Open("https://www.canva.com"); Respond("Opening Canva."); return; }
            if (text.Contains("chatgpt")) { Open("https://chatgpt.com"); Respond("Opening ChatGPT."); return; }
            if (text.Contains("browser") || text.Contains("google") || text.Contains("براؤزر") || text.Contains("ब्राउज़र")) { Open("https://www.google.com"); Respond("Opening the browser."); return; }
            if (text.Contains("calculator") || text.Contains("کیلکولیٹر") || text.Contains("कैलकुलेटर")) { Open("calc.exe"); Respond("Opening Calculator."); return; }
            if (text.Contains("notepad") || text.Contains("نوٹ پیڈ") || text.Contains("नोटपैड")) { Open("notepad.exe"); Respond("Opening Notepad."); return; }
            if (text.Contains("settings") || text.Contains("سیٹنگز") || text.Contains("सेटिंग")) { Open("ms-settings:"); Respond("Opening Windows Settings."); return; }
            if (text.Contains("explorer") || text.Contains("فائل ایکسپلورر") || text.Contains("फाइल एक्सप्लोरर")) { Open("explorer.exe"); Respond("Opening File Explorer."); return; }
            if (text.Contains("paint")) { Open("mspaint.exe"); Respond("Opening Paint."); return; }
            if (text.Contains("word")) { Open("winword.exe"); Respond("Opening Microsoft Word, if installed."); return; }
            if (text.Contains("excel")) { Open("excel.exe"); Respond("Opening Microsoft Excel, if installed."); return; }
            if (text.Contains("mt5") || text.Contains("metatrader")) { Respond("MT5 trading is not connected. I will not place real trades without an approved strategy and safeguards."); return; }
            if (text.Contains("screenshot") || text.Contains("اسکرین شاٹ") || text.Contains("स्क्रीनशॉट")) { TakeScreenshot(); return; }
            if (text.StartsWith("search ") || text.StartsWith("find ") || text.StartsWith("تلاش ") || text.StartsWith("खोजो "))
            {
                var query = text[(text.IndexOf(' ') + 1)..].Trim();
                Open("https://www.google.com/search?q=" + Uri.EscapeDataString(query)); Respond("Searching for " + query); return;
            }
            if (text.StartsWith("calculate "))
            {
                var expression = text["calculate ".Length..];
                if (!Regex.IsMatch(expression, @"^[0-9.+*/()\s-]{1,100}$")) { Respond("Only basic arithmetic is available."); return; }
                var result = new DataTable().Compute(expression, null);
                Respond("The result is " + result); return;
            }
            if (text.Contains("time") || text.Contains("وقت") || text.Contains("समय")) { Respond("The time is " + DateTime.Now.ToString("h:mm tt")); return; }
            if (text.Contains("date") || text.Contains("تاریخ") || text.Contains("तारीख")) { Respond("Today is " + DateTime.Now.ToString("dddd, MMMM d")); return; }
            if (text.Contains("how are you") || text.Contains("کیسے ہو") || text.Contains("कैसे हो")) { Respond("I'm ready to help, friend. What would you like to do?"); return; }
            if (text is "hi" or "hello" or "hey" or "سلام" or "नमस्ते") { Respond("Hello, friend. I'm listening."); return; }
            if (text.Contains("shutdown") || text.Contains("restart") || text.Contains("بند کرو") || text.Contains("रीस्टार्ट"))
            {
                var restart = text.Contains("restart") || text.Contains("ری اسٹارٹ") || text.Contains("रीस्टार्ट");
                var confirm = System.Windows.MessageBox.Show("Do you want to " + (restart ? "restart" : "shut down") + " Windows? Unsaved work may be lost.", "Confirm system action", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
                if (confirm == System.Windows.MessageBoxResult.Yes) { Process.Start(new ProcessStartInfo("shutdown.exe", (restart ? "/r" : "/s") + " /t 30") { UseShellExecute = true }); Respond("Windows action scheduled in 30 seconds."); }
                else Respond("Cancelled.");
                return;
            }
            Respond("I heard: " + raw + ". I don't have a safe action for that request yet.");
        }
        catch (Exception ex) { Respond("I couldn't complete that action: " + ex.Message); }
    }
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
    private static void PressMediaKey(byte key)
    {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }
    private void CloseInstalledApp(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested) || requested.Length > 70)
        { Respond("Please specify the app to close."); return; }
        var matches = Process.GetProcesses()
            .Where(p => p.ProcessName.Equals(requested, StringComparison.OrdinalIgnoreCase))
            .ToList();
        try
        {
            if (matches.Count == 0) { Respond("No running app matched " + requested); return; }
            if (matches.Count > 1) { Respond("Several processes match. Close the app manually to avoid losing work."); return; }
            var p = matches[0];
            var confirm = System.Windows.MessageBox.Show(
                "Ask " + p.ProcessName + " to close? Unsaved work may require your attention.",
                "Madi AI • Confirm close app", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) { Respond("Cancelled."); return; }
            if (p.CloseMainWindow()) Respond("Asked " + p.ProcessName + " to close.");
            else Respond("This app has no window that can be closed safely.");
        }
        finally { foreach (var p in matches) p.Dispose(); }
    }
    private void LaunchInstalledApp(string requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) { ShowAppPicker(); return; }
        if (requested.Length > 80 || requested.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        { Respond("Please say the name of an installed application."); return; }
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        var matches = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if (Directory.Exists(folder))
                    matches.AddRange(Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories)
                        .Where(p => Path.GetFileNameWithoutExtension(p).Contains(requested, StringComparison.OrdinalIgnoreCase)));
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        var exact = matches.FirstOrDefault(p => string.Equals(Path.GetFileNameWithoutExtension(p), requested, StringComparison.OrdinalIgnoreCase));
        var selected = exact ?? (matches.Count == 1 ? matches[0] : null);
        if (selected != null) { Open(selected); Respond("Opening " + Path.GetFileNameWithoutExtension(selected)); return; }
        if (matches.Count > 1)
        {
            Respond("Several applications match " + requested + ". Please give a more specific name."); return;
        }
        Respond("I could not find " + requested + " in the Windows Start Menu. Use Open App to select it.");
    }
    private void ShowAppPicker()
    {
        if (!unlocked) return;
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Madi AI • Select an application",
            Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (picker.ShowDialog() == true)
        {
            try { Open(picker.FileName); Respond("Opening " + Path.GetFileNameWithoutExtension(picker.FileName)); }
            catch (Exception ex) { Respond("Unable to open application: " + ex.Message); }
        }
    }

    private void TakeScreenshot()
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var file = Path.Combine(folder, "MadiAI-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        Respond("Screenshot saved to Pictures.");
    }
    private void UpdateStats()
    {
        ClockText.Text = DateTime.Now.ToString("hh:mm:ss tt");
        DateText.Text = DateTime.Now.ToString("ddd, MMM d, yyyy");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            DiskValue.Text = Math.Round(100 * (1 - drive.TotalFreeSpace / (double)drive.TotalSize)) + "%";
            var memory = new MEMORYSTATUSEX();
            memory.dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
            if (GlobalMemoryStatusEx(ref memory)) RamValue.Text = memory.dwMemoryLoad + "%";
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                var i = ToLong(idle); var k = ToLong(kernel); var u = ToLong(user);
                var deltaTotal = (k - oldKernel) + (u - oldUser);
                if (deltaTotal > 0 && oldKernel != 0)
                    CpuValue.Text = Math.Clamp(Math.Round(100 * (1 - (i - oldIdle) / (double)deltaTotal)), 0, 100) + "%";
                oldIdle = i; oldKernel = k; oldUser = u;
            }
        }
        catch { }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low; public uint High; }
    private static long ToLong(FILETIME t) => ((long)t.High << 32) | t.Low;
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [StructLayout(LayoutKind.Sequential)] private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    private async void Voice_Click(object sender, RoutedEventArgs e)
    {
        if (!unlocked) return;
        if (voicePreparing)
        {
            voiceSetupCancellation?.Cancel();
            Respond("Cancelling voice model download.", false);
            return;
        }
        if (listening)
        {
            listening = false;
            voiceChunk.Stop();
            try { microphone?.StopRecording(); } catch { }
            ListenStatus.Text = "● Standby";
            CoreState.Text = "MADI AI • ONLINE";
            MicButton.Content = "🎙";
            Respond("Voice control stopped.");
            return;
        }
        voicePreparing = true;
        voiceSetupCancellation = new CancellationTokenSource();
        try
        {
            ListenStatus.Text = "● Starting";
            CoreState.Text = "STARTING VOICE ENGINE";
            MicButton.Content = "×";
            await EnsureModelAsync(voiceSetupCancellation.Token);
            voiceSetupCancellation.Token.ThrowIfCancellationRequested();
            if (WaveIn.DeviceCount == 0)
                throw new InvalidOperationException("No microphone was found. Connect a microphone and enable Windows microphone access.");
            listening = true;
            ListenStatus.Text = "● Listening";
            CoreState.Text = "MADI AI • LISTENING";
            MicButton.Content = "■";
            speaker.SpeakAsyncCancelAll();
            StartRecording();
        }
        catch (OperationCanceledException)
        {
            ListenStatus.Text = "● Standby";
            CoreState.Text = "MADI AI • ONLINE";
            Respond("Voice setup cancelled.", false);
        }
        catch (Exception ex)
        {
            listening = false;
            ListenStatus.Text = "● Error";
            CoreState.Text = "VOICE UNAVAILABLE";
            Respond("Voice setup failed: " + ex.Message, false);
        }
        finally
        {
            voicePreparing = false;
            voiceSetupCancellation?.Dispose();
            voiceSetupCancellation = null;
            if (!listening) MicButton.Content = "🎙";
        }
    }
    private async Task EnsureModelAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(ModelFile) && new FileInfo(ModelFile).Length > 20_000_000)
        {
            Respond("Offline speech model is ready.", false);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ModelFile)!);
        var temp = ModelFile + ".download";
        try
        {
            Respond("Downloading the free speech model because the bundled copy was not found.", false);
            using var response = await http.GetAsync(
                "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin",
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var dest = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
            var buffer = new byte[81920];
            long received = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                received += count;
                if (received % (81920 * 16) < 81920)
                {
                    var mb = received / 1048576.0;
                    StatusText.Text = total is > 0
                        ? $"Downloading speech model: {mb:F1} MB / {total.Value / 1048576.0:F1} MB ({100.0 * received / total.Value:F0}%)"
                        : $"Downloading speech model: {mb:F1} MB";
                }
            }
            await dest.FlushAsync(cancellationToken);
            }
            if (new FileInfo(temp).Length < 20_000_000)
                throw new InvalidDataException("Speech model download was incomplete.");
            File.Move(temp, ModelFile, true);
            Respond("Speech model ready. Starting microphone.", false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Could not download the free speech model. Check your internet connection or place ggml-tiny.bin in %LOCALAPPDATA%\\MadiAI\\models. " + ex.Message, ex);
        }
        finally
        {
            TryDelete(temp);
        }
    }
    private void StartRecording()
    {
        if (!listening || processingAudio) return;
        currentRecording = Path.Combine(Path.GetTempPath(), "madi-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        microphone = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        waveWriter = new WaveFileWriter(currentRecording, microphone.WaveFormat);
        microphone.DataAvailable += (_, e) => waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        microphone.RecordingStopped += async (_, _) =>
        {
            voiceChunk.Stop();
            var file = currentRecording;
            try { waveWriter?.Dispose(); microphone?.Dispose(); } catch { }
            waveWriter = null; microphone = null;
            if (!listening || file == null) { if (file != null) TryDelete(file); return; }
            processingAudio = true;
            try { await TranscribeAsync(file); }
            catch (Exception ex) { Respond("Voice recognition error: " + ex.Message); }
            finally { processingAudio = false; TryDelete(file); if (listening) StartRecording(); }
        };
        microphone.StartRecording();
        voiceChunk.Start();
    }
    private async Task TranscribeAsync(string wavPath)
    {
        Respond("Processing speech locally...", false);
        using var factory = WhisperFactory.FromPath(ModelFile);
        var selected = LanguagePicker.SelectedIndex;
        using var processor = factory.CreateBuilder().WithLanguage(selected == 1 ? "en" : selected == 2 ? "ur" : selected == 3 ? "hi" : "auto").Build();
        using var stream = File.OpenRead(wavPath);
        var transcript = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream))
            transcript.Append(segment.Text).Append(' ');
        var recognized = transcript.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(recognized)) { CommandBox.Text = recognized; Execute(recognized); }
        else Respond("Listening...", false);
        ListenStatus.Text = listening ? "● Listening" : "● Standby";
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private void Browser_Click(object sender, RoutedEventArgs e) => Execute("open browser");
    private void Youtube_Click(object sender, RoutedEventArgs e) => Execute("open youtube");
    private void Whatsapp_Click(object sender, RoutedEventArgs e) => Execute("open whatsapp");
    private void Ebay_Click(object sender, RoutedEventArgs e) => Execute("open ebay");
    private void Ali_Click(object sender, RoutedEventArgs e) => Execute("open aliexpress");
    private void Canva_Click(object sender, RoutedEventArgs e) => Execute("open canva");
    private void Mt5_Click(object sender, RoutedEventArgs e) => Execute("open mt5");
    private void Chat_Click(object sender, RoutedEventArgs e) => Execute("open chatgpt");
    private void Office_Click(object sender, RoutedEventArgs e) => Execute("open word");
    private void Calc_Click(object sender, RoutedEventArgs e) => Execute("open calculator");
    private void Screenshot_Click(object sender, RoutedEventArgs e) => Execute("screenshot");
    private void System_Click(object sender, RoutedEventArgs e) => Execute("open settings");
    private void Apps_Click(object sender, RoutedEventArgs e) => ShowAppPicker();
    private void Web_Click(object sender, RoutedEventArgs e) => Execute("open browser");
    private void Files_Click(object sender, RoutedEventArgs e)
    {
        if (!unlocked) return;
        var dialog = new Microsoft.Win32.OpenFileDialog();
        if (dialog.ShowDialog() == true) { try { Open(dialog.FileName); Respond("Opening your selected file."); } catch (Exception ex) { Respond(ex.Message); } }
    }
    private void Tasks_Click(object sender, RoutedEventArgs e) => Respond("Custom task routines are planned, but not enabled yet.");
    private void Settings_Click(object sender, RoutedEventArgs e) => Respond("Your secret code stays on this computer and is preserved across application updates. Voice language can be selected at the top right.");
}
