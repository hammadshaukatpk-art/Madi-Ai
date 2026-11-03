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
    private string ModelFile => Path.Combine(profile, "models", "ggml-tiny.bin");
    private WaveInEvent? microphone;
    private WaveFileWriter? waveWriter;
    private string? currentRecording;
    private bool listening;
    private bool unlocked;
    private bool processingAudio;
    private long oldIdle, oldKernel, oldUser;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(profile);
        GatePrompt.Text = File.Exists(AuthFile) ? "Tell me your secret code to open me" : "Create your secret code to open me";
        GateHelp.Text = File.Exists(AuthFile) ? "Enter your saved code. It remains stored on this PC." : "Set your secret code once. It will be kept on this PC through app updates.";
        Loaded += (_, _) => { Animate(); Speak("Tell me your secret code to open me"); SecretBox.Focus(); };
        stats.Tick += (_, _) => UpdateStats();
        voiceChunk.Tick += (_, _) => { voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } };
        stats.Start();
        UpdateStats();
        Closed += (_, _) => { listening = false; voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } speaker.Dispose(); http.Dispose(); };
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
        if (code.Length < 6) { GateHelp.Text = "Use at least 6 characters."; return; }
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
            Respond("Welcome, Boss Hammad. Madi AI is online and ready for your commands.");
        }
        catch (Exception ex) { GateHelp.Text = "Unable to open profile: " + ex.Message; }
    }
    private void Unlock_Click(object sender, RoutedEventArgs e) => Unlock();
    private void SecretBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Unlock(); }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void CommandBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Execute(CommandBox.Text); }
    private void Send_Click(object sender, RoutedEventArgs e) => Execute(CommandBox.Text);

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    private static string Normalize(string raw)
    {
        var s = raw.Trim().ToLowerInvariant();
        foreach (var prefix in new[] { "please ", "madi ", "madi ai ", "hey madi ", "یار ", "براہ کرم ", "कृपया " })
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
            if (text.Contains("how are you") || text.Contains("کیسے ہو") || text.Contains("कैसे हो")) { Respond("I'm ready to help, Boss Hammad. What would you like to do?"); return; }
            if (text is "hi" or "hello" or "hey" or "سلام" or "नमस्ते") { Respond("Hello, Boss Hammad. I'm listening."); return; }
            if (text.Contains("shutdown") || text.Contains("restart") || text.Contains("بند کرو") || text.Contains("रीस्टार्ट"))
            {
                var restart = text.Contains("restart") || text.Contains("ری اسٹارٹ") || text.Contains("रीस्टार्ट");
                var confirm = MessageBox.Show("Do you want to " + (restart ? "restart" : "shut down") + " Windows? Unsaved work may be lost.", "Confirm system action", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes) { Open("shutdown.exe " + (restart ? "/r" : "/s") + " /t 30"); Respond("Windows action scheduled in 30 seconds."); }
                else Respond("Cancelled.");
                return;
            }
            Respond("I heard: " + raw + ". I don't have a safe action for that request yet.");
        }
        catch (Exception ex) { Respond("I couldn't complete that action: " + ex.Message); }
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
        if (listening) { listening = false; voiceChunk.Stop(); microphone?.StopRecording(); ListenStatus.Text = "● Standby"; CoreState.Text = "MADI AI • ONLINE"; Respond("Voice control stopped."); return; }
        try
        {
            ListenStatus.Text = "● Preparing";
            CoreState.Text = "PREPARING VOICE MODEL";
            await EnsureModelAsync();
            listening = true;
            ListenStatus.Text = "● Listening";
            CoreState.Text = "MADI AI • LISTENING";
            StartRecording();
        }
        catch (Exception ex) { listening = false; ListenStatus.Text = "● Standby"; Respond("Voice initialization failed: " + ex.Message); }
    }
    private async Task EnsureModelAsync()
    {
        if (File.Exists(ModelFile) && new FileInfo(ModelFile).Length > 20_000_000) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ModelFile)!);
        var temp = ModelFile + ".download";
        Respond("Downloading free multilingual speech model for the first time. Please wait.", false);
        using var response = await http.GetAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var dest = File.Create(temp)) await source.CopyToAsync(dest);
        File.Move(temp, ModelFile, true);
        Respond("Offline voice model is ready.", false);
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
    private void Apps_Click(object sender, RoutedEventArgs e) => Respond("Say an app name, for example: open calculator, notepad, Word or browser.");
    private void Web_Click(object sender, RoutedEventArgs e) => Respond("Say search followed by your search words.");
    private void Files_Click(object sender, RoutedEventArgs e)
    {
        if (!unlocked) return;
        var dialog = new OpenFileDialog();
        if (dialog.ShowDialog() == true) { try { Open(dialog.FileName); Respond("Opening your selected file."); } catch (Exception ex) { Respond(ex.Message); } }
    }
    private void Tasks_Click(object sender, RoutedEventArgs e) => Respond("Custom task routines are planned, but not enabled yet.");
    private void Settings_Click(object sender, RoutedEventArgs e) => Respond("Your secret code stays on this computer and is preserved across application updates. Voice language can be selected at the top right.");
}
