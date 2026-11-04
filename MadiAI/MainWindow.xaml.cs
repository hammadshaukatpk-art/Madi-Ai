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
    private readonly DispatcherTimer voiceChunk = new() { Interval = TimeSpan.FromSeconds(14) };
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
    private WhisperFactory? voiceFactory;
    private int stopRequested;
    private DateTime captureStarted;
    private DateTime firstSpeech;
    private DateTime lastSpeech;
    private int speechFrames;
    private int micDeviceNumber;
    private List<ChromeProfile>? pendingChromeProfiles;
    private string pendingChromeUrl = "https://www.google.com";
    private string pendingChromeService = "Google";
    private bool voicePreparing;
    private bool chatPending;
    private bool installingLocalAi;
    private readonly List<(string Role, string Content)> chatHistory = new();
    private LocalMemoryStore memory = null!;
    private string? selectedModel;
    private double smoothedCpu = -1;
    private string ChatHistoryFile => Path.Combine(profile, "conversation.json");
    private string SelectedModelFile => Path.Combine(profile, "selected-model.txt");
    private CancellationTokenSource? voiceSetupCancellation;
    private long oldIdle, oldKernel, oldUser;

    public MainWindow()
    {
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(1440, SystemParameters.WorkArea.Width - 24));
        Height = Math.Max(MinHeight, Math.Min(850, SystemParameters.WorkArea.Height - 24));
        Directory.CreateDirectory(profile);
        memory = new LocalMemoryStore(profile);
        LoadConversationHistory();
        try
        {
            var settingsFile = Path.Combine(profile, "settings.json");
            if (File.Exists(settingsFile))
            {
                using var config = JsonDocument.Parse(File.ReadAllText(settingsFile));
                if (config.RootElement.TryGetProperty("microphone", out var mic))
                    micDeviceNumber = mic.GetInt32();
            }
        }
        catch (Exception) { micDeviceNumber = 0; }
        GatePrompt.Text = File.Exists(AuthFile) ? "Enter your private secret code" : "Secret code not configured";
        GateHelp.Text = File.Exists(AuthFile) ? "Use the code you created in Madi AI Setup. It is stored only on this PC." : "Please run Madi AI Setup to create your secret code before using the app.";
        Loaded += (_, _) => { Animate(); SecretBox.Focus(); };
        stats.Tick += (_, _) => UpdateStats();
        voiceChunk.Tick += (_, _) => { voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } };
        stats.Start();
        UpdateStats();
        Closed += (_, _) => { voiceSetupCancellation?.Cancel(); listening = false; voiceChunk.Stop(); try { microphone?.StopRecording(); } catch { } voiceFactory?.Dispose(); speaker.Dispose(); http.Dispose(); };
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
    private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !unlocked) return;
        var language = LanguagePicker.SelectedIndex switch { 1 => "English", 2 => "Urdu", 3 => "Hindi", _ => "Automatic" };
        Respond("Voice recognition language: " + language + ". The change applies to your next command.", false);
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
        AppendConversation("Madi", message);
        if (speak) Speak(message);
    }

    private void AppendConversation(string who, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ConversationText.AppendText($"{who}: {message.Trim()}\n\n");
        ConversationText.ScrollToEnd();
    }

    private void LoadConversationHistory()
    {
        try
        {
            if (File.Exists(SelectedModelFile))
                selectedModel = File.ReadAllText(SelectedModelFile).Trim();
            if (!File.Exists(ChatHistoryFile)) return;
            using var data = JsonDocument.Parse(File.ReadAllText(ChatHistoryFile));
            foreach (var item in data.RootElement.EnumerateArray().TakeLast(100))
            {
                var role = item.GetProperty("role").GetString();
                var content = item.GetProperty("content").GetString();
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(content))
                    chatHistory.Add((role, content));
            }
            foreach (var entry in chatHistory.TakeLast(12))
                AppendConversation(entry.Role == "user" ? "You" : "Madi", entry.Content);
        }
        catch (Exception) { chatHistory.Clear(); }
    }

    private void SaveConversationHistory()
    {
        try
        {
            var entries = chatHistory.TakeLast(100).Select(e => new { role = e.Role, content = e.Content });
            var temp = ChatHistoryFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries));
            File.Move(temp, ChatHistoryFile, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task RefreshLocalAiStatusAsync()
    {
        var models = await GetInstalledModelsAsync();
        if (!unlocked) return;
        if (models.Count == 0)
        {
            LocalAiModuleText.Text = "◌  Local AI • Ollama unavailable";
            return;
        }
        var model = models.FirstOrDefault(m => m.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(m => m.Equals("llama3.2:latest", StringComparison.OrdinalIgnoreCase))
            ?? models[0];
        LocalAiModuleText.Text = "●  Local AI • " + model;
    }

    private async Task<List<string>> GetInstalledModelsAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var response = await http.GetAsync("http://127.0.0.1:11434/api/tags", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return json.RootElement.GetProperty("models").EnumerateArray()
                .Select(m => m.GetProperty("name").GetString())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
        {
            return new List<string>();
        }
    }

    private void Unlock()
    {
        var code = SecretBox.Password;
        if (!File.Exists(AuthFile)) { GateHelp.Text = "Run Madi AI Setup first to create your private secret code."; return; }
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

            unlocked = true;
            SecretBox.Clear();
            GateOverlay.Visibility = Visibility.Collapsed;
            CoreState.Text = "MADI AI • ONLINE";
            Respond("Madi AI unlocked. Voice control is starting.", false);
            _ = RefreshLocalAiStatusAsync();
            Dispatcher.BeginInvoke(new Action(() => Voice_Click(this, new RoutedEventArgs())));
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
        // Speech models often split brand names differently in Hindi, Urdu and English.
        // Normalize common transcriptions before matching explicit application commands.
        foreach (var variant in new (string From, string To)[]
        {
            ("what's app", "whatsapp"), ("whats app", "whatsapp"),
            ("what s app", "whatsapp"), ("व्हाट्स ऐप", "whatsapp"),
            ("व्हाट्सएप", "whatsapp"), ("व्हाट्सऐप", "whatsapp"),
            ("व्हाट्स एप", "whatsapp"), ("वॉट्सएप", "whatsapp"),
            ("व्हाट्सएप्प", "whatsapp"), ("واٹس ایپ", "whatsapp"),
            ("واٹس اپ", "whatsapp"), ("واٹساپ", "whatsapp"),
            ("यूट्यूब", "youtube"), ("यू ट्यूब", "youtube"),
            ("یوٹیوب", "youtube"), ("یو ٹیوب", "youtube"),
            ("गूगल", "google"), ("گوگل", "google"),
            ("क्रोम", "chrome"), ("کروم", "chrome"),
            ("कैनवा", "canva"), ("کینوا", "canva"),
            ("मेटाट्रेडर", "metatrader"), ("میٹا ٹریڈر", "metatrader"),
            ("अलीएक्सप्रेस", "aliexpress"), ("علی ایکسپریس", "aliexpress")
        }) s = s.Replace(variant.From, variant.To, StringComparison.Ordinal);
        foreach (var prefix in new[] { "hey madi ai ", "hey madi ", "madi ai ", "madi ", "please ", "یار ", "براہ کرم ", "कृपया " })
            if (s.StartsWith(prefix)) s = s[prefix.Length..];
        return s;
    }
    private void Execute(string raw)
    {
        if (!unlocked || string.IsNullOrWhiteSpace(raw)) return;
        var text = Normalize(raw);
        AppendConversation("You", raw);
        CommandBox.Text = "";
        try
        {
            if (TrySelectPendingChromeProfile(text)) return;
            if (TryHandleMemoryCommand(text, raw)) return;
            if (TryHandleExtendedWindowsCommand(text, raw)) return;
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
            if (text.Contains("youtube") || text.Contains("یوٹیوب") || text.Contains("यूट्यूब")) { OpenWebWithProfile("YouTube", "https://www.youtube.com", text); return; }
            if (text.Contains("whatsapp") || text.Contains("whats app") || text.Contains("واٹس ایپ") || text.Contains("واتس اپ") || text.Contains("व्हाट्सएप") || text.Contains("व्हाट्स ऐप") || text.Contains("व्हाट्सऐप"))
            {
                if (text.Contains("desktop") || text.Contains("app only") || text.Contains("ڈیسک ٹاپ") || text.Contains("डेस्कटॉप"))
                {
                    if (TryLaunchShortcut("WhatsApp", out _)) Respond("Opening WhatsApp desktop.");
                    else Respond("WhatsApp desktop was not found. Say open WhatsApp to choose a Chrome profile.");
                }
                else OpenWebWithProfile("WhatsApp", "https://web.whatsapp.com", text);
                return;
            }
            if (text.Contains("aliexpress") || text.Contains("علی ایکسپریس") || text.Contains("अलीएक्सप्रेस")) { OpenWebWithProfile("AliExpress", "https://www.aliexpress.com", text); return; }
            if (text.Contains("ebay") || text.Contains("ای بے") || text.Contains("ईबे")) { OpenWebWithProfile("eBay", "https://www.ebay.com", text); return; }
            if (text.Contains("canva") || text.Contains("کینوا") || text.Contains("कैनवा")) { OpenWebWithProfile("Canva", "https://www.canva.com", text); return; }
            if (text.Contains("chatgpt")) { Open("https://chatgpt.com"); Respond("Opening ChatGPT."); return; }
            if (text.Contains("browser") || text.Contains("google") || text.Contains("گوگل") || text.Contains("गूगल") || text.Contains("chrome") || text.Contains("براؤزر") || text.Contains("ब्राउज़र"))
            { OpenGoogleWithProfile(text); return; }
            if (text.Contains("calculator") || text.Contains("کیلکولیٹر") || text.Contains("कैलकुलेटर")) { Open("calc.exe"); Respond("Opening Calculator."); return; }
            if (text.Contains("notepad") || text.Contains("نوٹ پیڈ") || text.Contains("नोटपैड")) { Open("notepad.exe"); Respond("Opening Notepad."); return; }
            if (text.Contains("settings") || text.Contains("سیٹنگز") || text.Contains("सेटिंग")) { Open("ms-settings:"); Respond("Opening Windows Settings."); return; }
            if (text.Contains("explorer") || text.Contains("فائل ایکسپلورر") || text.Contains("फाइल एक्सप्लोरर")) { Open("explorer.exe"); Respond("Opening File Explorer."); return; }
            if (text.Contains("paint")) { Open("mspaint.exe"); Respond("Opening Paint."); return; }
            if (text.Contains("word")) { Open("winword.exe"); Respond("Opening Microsoft Word, if installed."); return; }
            if (text.Contains("excel")) { Open("excel.exe"); Respond("Opening Microsoft Excel, if installed."); return; }
            if (text.Contains("mt5") || text.Contains("metatrader") || text.Contains("میٹا ٹریڈر") || text.Contains("मेटाट्रेडर"))
            {
                if (TryLaunchShortcut("MetaTrader 5", out _) || TryLaunchShortcut("MT5", out _))
                    Respond("Opening MetaTrader 5.");
                else Respond("MetaTrader 5 wasn't found in the Start Menu. You can select its terminal64.exe using Open App.");
                return;
            }
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
            _ = RouteUnknownAsync(raw, text);
        }
        catch (Exception ex) { Respond("I couldn't complete that action: " + ex.Message); }
    }

    private async Task RouteUnknownAsync(string original, string normalized)
    {
        // Ordinary conversation goes directly to streaming chat. Only an
        // apparent command is classified, so chit-chat stays responsive.
        var looksLikeCommand = Regex.IsMatch(normalized,
            @"^(?:please |can you |could you |would you |madi |open |launch |start |show |bring |switch |focus |search |find |kholo |khol do |کھولو |खोलो )") ||
            normalized.Contains(" kholo") || normalized.Contains(" खोलो") || normalized.Contains(" کھولو");
        if (looksLikeCommand && !chatPending)
        {
            try
            {
                var models = await GetInstalledModelsAsync();
                if (models.Count > 0)
                {
                    var model = models.FirstOrDefault(m => m.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
                        ?? models.FirstOrDefault(m => m.Equals("llama3.2:latest", StringComparison.OrdinalIgnoreCase))
                        ?? models[0];
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(22));
                    var plan = await LocalActionPlanner.PlanAsync(http, model, original, timeout.Token);
                    if (plan != null && TryPerformPlannedAction(plan)) return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException)
            {
                // Local command planner is optional; regular chat remains available.
            }
        }
        await AskLocalAiAsync(original);
    }

    private bool TryPerformPlannedAction(LocalActionPlanner.ActionPlan plan)
    {
        switch (plan.Action)
        {
            case "open_google": OpenGoogleWithProfile("open google"); return true;
            case "open_youtube": OpenWebWithProfile("YouTube", "https://www.youtube.com", "open youtube"); return true;
            case "open_downloads":
                Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
                Respond("Opening Downloads.", false); return true;
            case "open_documents": Open(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); Respond("Opening Documents.", false); return true;
            case "open_desktop": Open(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); Respond("Opening Desktop.", false); return true;
            case "open_pictures": Open(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)); Respond("Opening Pictures.", false); return true;
            case "open_calculator": Open("calc.exe"); Respond("Opening Calculator.", false); return true;
            case "open_notepad": Open("notepad.exe"); Respond("Opening Notepad.", false); return true;
            case "open_settings": Open("ms-settings:"); Respond("Opening Windows Settings.", false); return true;
            case "open_explorer": Open("explorer.exe"); Respond("Opening File Explorer.", false); return true;
            case "open_mt5":
                if (TryLaunchShortcut("MetaTrader 5", out _) || TryLaunchShortcut("MT5", out _))
                    Respond("Opening MetaTrader 5.", false);
                else Respond("MT5 was not found in Start Menu. Use the application picker to locate terminal64.exe.", false);
                return true;
            case "mt5_status": return TryHandleExtendedWindowsCommand("mt5 status", "mt5 status");
            case "search_web" when !string.IsNullOrWhiteSpace(plan.Argument):
                Open("https://www.google.com/search?q=" + Uri.EscapeDataString(plan.Argument));
                Respond("Searching the web for " + plan.Argument, false); return true;
            case "search_youtube" when !string.IsNullOrWhiteSpace(plan.Argument):
                Open("https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(plan.Argument));
                Respond("Searching YouTube for " + plan.Argument, false); return true;
            case "open_app" when !string.IsNullOrWhiteSpace(plan.Argument):
                LaunchInstalledApp(plan.Argument); return true;
            case "focus_app" when !string.IsNullOrWhiteSpace(plan.Argument):
                return TryHandleExtendedWindowsCommand("focus " + plan.Argument, "focus " + plan.Argument);
            default: return false;
        }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    private bool TryHandleExtendedWindowsCommand(string text, string raw)
    {
        if (text.StartsWith("copy ") && text.EndsWith(" to clipboard"))
        {
            var value = raw.Trim();
            var start = value.IndexOf("copy ", StringComparison.OrdinalIgnoreCase);
            var end = value.LastIndexOf(" to clipboard", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start + 5)
            {
                System.Windows.Clipboard.SetText(value.Substring(start + 5, end - start - 5));
                Respond("Copied the text to your clipboard.", false);
                return true;
            }
        }
        if (text is "read clipboard" or "what is in my clipboard" or "show clipboard")
        {
            var value = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
            Respond(value.Length == 0 ? "There is no text in the clipboard." :
                "Clipboard text: " + (value.Length > 600 ? value[..600] + "…" : value), false);
            return true;
        }
        if (text is "running apps" or "show running apps" or "what apps are open" or "list open windows")
        {
            var apps = Process.GetProcesses().Where(p =>
            {
                try { return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(p.MainWindowTitle); }
                catch { return false; }
            }).Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).Take(25).ToArray();
            Respond(apps.Length == 0 ? "I couldn't find any visible app windows." :
                "Visible running apps: " + string.Join(", ", apps), false);
            return true;
        }
        if (text.StartsWith("switch to ") || text.StartsWith("focus ") || text.StartsWith("bring ") && text.EndsWith(" to front"))
        {
            var target = text.StartsWith("switch to ") ? text[10..] :
                text.StartsWith("focus ") ? text[6..] : text[6..^9];
            target = target.Trim();
            if (target.Length < 2 || target.Length > 60) { Respond("Tell me which running app to focus.", false); return true; }
            Process? match = null;
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero &&
                        (process.ProcessName.Contains(target, StringComparison.OrdinalIgnoreCase) ||
                         process.MainWindowTitle.Contains(target, StringComparison.OrdinalIgnoreCase)))
                    { match = process; break; }
                }
                catch { }
            }
            if (match == null) { Respond("I couldn't find an open window matching " + target, false); return true; }
            ShowWindow(match.MainWindowHandle, 9);
            var focused = SetForegroundWindow(match.MainWindowHandle);
            Respond(focused ? "Switched to " + match.ProcessName : "Found " + match.ProcessName + ", but Windows blocked focus switching.", false);
            return true;
        }
        if (text is "mt5 status" or "metatrader status" or "is mt5 running" or "check mt5")
        {
            var terminals = Process.GetProcesses().Where(p =>
                p.ProcessName.Equals("terminal64", StringComparison.OrdinalIgnoreCase) ||
                p.ProcessName.Equals("terminal", StringComparison.OrdinalIgnoreCase)).ToArray();
            Respond(terminals.Length == 0 ? "MetaTrader 5 is not currently running." :
                $"Detected {terminals.Length} MetaTrader terminal process(es). I cannot verify your broker login, positions or balances without a dedicated terminal integration.", false);
            return true;
        }
        if (text is "system information" or "system status" or "pc status" or "computer status")
        {
            UpdateStats();
            Respond($"Windows system status: CPU {CpuValue.Text}, RAM {RamValue.Text}, system disk used {DiskValue.Text}.", false);
            return true;
        }
        if (text is "open desktop" or "desktop folder")
        {
            Open(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            Respond("Opened your Desktop folder.", false);
            return true;
        }
        return false;
    }

    private bool TryHandleMemoryCommand(string normalized, string original)
    {
        if (normalized is "what do you remember" or "show memory" or "show my memory" or "what do you remember about me" ||
            normalized.Contains("kya yaad hai") || normalized.Contains("क्या याद है") || normalized.Contains("کیا یاد ہے"))
        {
            Respond(memory.Items.Count == 0
                ? "I haven't saved any personal memories yet. Say 'remember that ...' to save a preference."
                : "Your private saved memories:\n" + string.Join("\n", memory.Items.Select((item, i) => $"{i + 1}. {item}")), false);
            return true;
        }
        if (normalized is "clear memory" or "delete all memories" or "forget everything")
        {
            var answer = System.Windows.MessageBox.Show("Permanently delete all Madi AI saved memories on this PC?", "Confirm local memory deletion",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (answer == System.Windows.MessageBoxResult.Yes) { memory.Clear(); Respond("All Madi AI saved memories were deleted from this PC.", false); }
            else Respond("Memory deletion cancelled.", false);
            return true;
        }
        var remember = Regex.Match(original.Trim(), @"(?i)^(?:madi(?: ai)?[, ]+)?(?:please )?(?:remember that|remember|yaad rakh(?:o)?(?: ke)?|याद रखो(?: कि)?|یاد رکھو(?: کہ)?)\s+(.+)$");
        if (remember.Success)
        {
            memory.TryRemember(remember.Groups[1].Value, out var feedback);
            Respond(feedback, false);
            return true;
        }
        var forget = Regex.Match(original.Trim(), @"(?i)^(?:madi(?: ai)?[, ]+)?(?:please )?(?:forget that|forget|bhool jao|भूल जाओ|بھول جاؤ)\s+(.+)$");
        if (forget.Success)
        {
            var count = memory.Forget(forget.Groups[1].Value);
            Respond(count == 0 ? "I couldn't find a matching saved memory." : $"Removed {count} matching saved memory item(s).", false);
            return true;
        }
        return false;
    }

    private static string? FindOllama()
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
        };
        var installed = paths.FirstOrDefault(File.Exists);
        if (installed != null) return installed;
        var env = Environment.GetEnvironmentVariable("PATH") ?? "";
        return env.Split(Path.PathSeparator).Select(p => Path.Combine(p.Trim('"'), "ollama.exe")).FirstOrDefault(File.Exists);
    }

    private async Task AskLocalAiAsync(string question)
    {
        if (chatPending) { Respond("I am still answering your previous question.", false); return; }
        chatPending = true;
        try
        {
            CoreState.Text = "MADI AI • THINKING";
            var models = await GetInstalledModelsAsync();
            if (models.Count == 0)
            {
                LocalAiModuleText.Text = "◌  Local AI • disconnected";
                Respond("Ollama is not reachable or no model is installed. Start Ollama and check Settings.", false);
                return;
            }
            var model = models.FirstOrDefault(m => m.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(m => m.Equals("llama3.2:latest", StringComparison.OrdinalIgnoreCase))
                ?? models.FirstOrDefault(m => m.StartsWith("llama3.2", StringComparison.OrdinalIgnoreCase))
                ?? models[0];
            LocalAiModuleText.Text = "●  Local AI • " + model;
            var messages = new List<object>
            {
                new { role = "system", content = "You are Madi AI, a conversational Windows desktop assistant. Speak naturally, briefly and warmly in the same language and script as the user, including Roman Urdu. You have no independent awareness or feelings. Never claim an action succeeded unless the application confirmed it. Ask one clear question if a command is ambiguous. Never invent PC access, trading results, app controls, or live information. The user can manage memory with remember that, show memory, forget, and clear memory. " + memory.BuildPrompt() }
            };
            foreach (var entry in chatHistory.TakeLast(12))
                messages.Add(new { role = entry.Role, content = entry.Content });
            messages.Add(new { role = "user", content = question });
            var payload = JsonSerializer.Serialize(new { model, stream = true, messages, options = new { num_predict = 240 } });
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/chat")
            { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            var answer = new StringBuilder();
            ConversationText.AppendText("Madi: ");
            var lastUiUpdate = DateTime.UtcNow;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var chunk = JsonDocument.Parse(line);
                if (chunk.RootElement.TryGetProperty("error", out var error))
                    throw new InvalidOperationException(error.GetString());
                if (chunk.RootElement.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    var part = content.GetString();
                    if (!string.IsNullOrEmpty(part))
                    {
                        answer.Append(part);
                        ConversationText.AppendText(part);
                        if ((DateTime.UtcNow - lastUiUpdate).TotalMilliseconds > 120)
                        {
                            ConversationText.ScrollToEnd();
                            var current = answer.ToString();
                            StatusText.Text = current.Length > 150 ? current[^150..] : current;
                            lastUiUpdate = DateTime.UtcNow;
                        }
                    }
                }
                if (chunk.RootElement.TryGetProperty("done", out var done) && done.GetBoolean()) break;
            }
            ConversationText.AppendText("\n\n");
            ConversationText.ScrollToEnd();
            var result = answer.ToString().Trim();
            if (result.Length == 0) { Respond("The local model returned an empty answer.", false); return; }
            chatHistory.Add(("user", question));
            chatHistory.Add(("assistant", result));
            if (chatHistory.Count > 100) chatHistory.RemoveRange(0, chatHistory.Count - 100);
            SaveConversationHistory();
            StatusText.Text = result;
            Speak(result);
        }
        catch (HttpRequestException)
        {
            LocalAiModuleText.Text = "◌  Local AI • disconnected";
            Respond("Ollama is not reachable. Start Ollama and try again.", false);
        }
        catch (TaskCanceledException)
        {
            Respond("The local model took too long. Try a smaller model in Settings.", false);
        }
        catch (Exception ex)
        {
            Respond("Local AI error: " + ex.Message, false);
        }
        finally { chatPending = false; CoreState.Text = listening ? "MADI AI • LISTENING" : "MADI AI • ONLINE"; }
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
    private bool TryLaunchShortcut(string requested, out string launched)
    {
        launched = "";
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
        };
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder)) continue;
                var matches = Directory.EnumerateFiles(folder, "*.lnk", SearchOption.AllDirectories)
                    .Where(p => Path.GetFileNameWithoutExtension(p).Contains(requested, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var selected = matches.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(requested, StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault();
                if (selected != null)
                {
                    Open(selected);
                    launched = Path.GetFileNameWithoutExtension(selected);
                    return true;
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return false;
    }

    private sealed record ChromeProfile(string Folder, string Name);
    private static List<ChromeProfile> GetChromeProfiles()
    {
        var profiles = new List<ChromeProfile>();
        var state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Google", "Chrome", "User Data", "Local State");
        if (!File.Exists(state)) return profiles;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(state));
            var cache = doc.RootElement.GetProperty("profile").GetProperty("info_cache");
            foreach (var entry in cache.EnumerateObject())
            {
                var name = entry.Value.TryGetProperty("name", out var n) ? n.GetString() : null;
                profiles.Add(new ChromeProfile(entry.Name, string.IsNullOrWhiteSpace(name) ? entry.Name : name));
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return profiles.OrderBy(p => p.Folder == "Default" ? 0 : 1).ThenBy(p => p.Folder).ToList();
    }
    private static string? FindChrome()
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        return dirs.Select(d => Path.Combine(d, "Google", "Chrome", "Application", "chrome.exe"))
            .FirstOrDefault(File.Exists);
    }
    private void OpenGoogleWithProfile(string command) => OpenWebWithProfile("Google", "https://www.google.com", command);
    private void OpenWebWithProfile(string service, string url, string command)
    {
        var chrome = FindChrome();
        if (chrome == null)
        {
            Open(url);
            Respond("Opening " + service + " in your default browser. Google Chrome is not installed.");
            return;
        }
        var profiles = GetChromeProfiles();
        ChromeProfile? selected = null;
        var named = Regex.Match(command, @"(?:profile|پروفائل|प्रोफ़ाइल|प्रोफाइल)\s*(?:number\s*)?([0-9]+)", RegexOptions.IgnoreCase);
        if (named.Success)
        {
            var n = named.Groups[1].Value;
            selected = profiles.FirstOrDefault(p => p.Folder.Equals("Profile " + n, StringComparison.OrdinalIgnoreCase))
                ?? profiles.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        }
        if (selected == null && profiles.Count > 1)
        {
            pendingChromeProfiles = profiles;
            pendingChromeUrl = url;
            pendingChromeService = service;
            var options = string.Join(", ", profiles.Take(8).Select((p, i) => $"Profile {i + 1}: {p.Name}"));
            Respond("Which Chrome profile for " + service + "? " + options + ". Say profile 1, profile 2, or the profile name.");
            return;
        }
        else selected ??= profiles.FirstOrDefault();
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
        if (selected != null) start.ArgumentList.Add("--profile-directory=" + selected.Folder);
        start.ArgumentList.Add(url);
        Process.Start(start);
        Respond(selected == null ? "Opening " + service + " in Chrome." : "Opening " + service + " in Chrome profile " + selected.Name + ".");
    }
    private bool TrySelectPendingChromeProfile(string command)
    {
        if (pendingChromeProfiles == null) return false;
        var profiles = pendingChromeProfiles;
        var numbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["one"]=1,["two"]=2,["three"]=3,["four"]=4,["five"]=5,
            ["एक"]=1,["दो"]=2,["तीन"]=3,["چار"]=4,["ایک"]=1,["دو"]=2,["تین"]=3
        };
        var match = Regex.Match(command, @"(?:(?:profile|پروفائل|प्रोफाइल|प्रोफ़ाइल)\s*(?:number\s*)?)?(\d+|one|two|three|four|five|एक|दो|तीन|ایک|دو|تین)", RegexOptions.IgnoreCase);
        int index = -1;
        if (match.Success)
        {
            var word = match.Groups[1].Value;
            index = int.TryParse(word, out var n) ? n - 1 : numbers.TryGetValue(word, out n) ? n - 1 : -1;
        }
        if (index < 0) index = profiles.FindIndex(p => command.Contains(p.Name.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= profiles.Count)
        {
            if (command is "cancel" or "stop" or "रद्द" or "منسوخ") { pendingChromeProfiles = null; Respond("Chrome profile selection cancelled.", false); return true; }
            return false;
        }
        pendingChromeProfiles = null;
        var targetUrl = pendingChromeUrl;
        var serviceName = pendingChromeService;
        var chrome = FindChrome();
        if (chrome == null) { Respond("Google Chrome is no longer installed."); return true; }
        var selected = profiles[index];
        var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
        start.ArgumentList.Add("--profile-directory=" + selected.Folder);
        start.ArgumentList.Add(targetUrl);
        Process.Start(start);
        Respond("Opening " + serviceName + " in Chrome profile " + selected.Name + ".");
        return true;
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
                {
                    var currentCpu = Math.Clamp(100 * (1 - (i - oldIdle) / (double)deltaTotal), 0, 100);
                    smoothedCpu = smoothedCpu < 0 ? currentCpu : smoothedCpu * 0.65 + currentCpu * 0.35;
                    CpuValue.Text = Math.Round(smoothedCpu) + "%";
                }
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
            StartVoiceButton.Content = "START";
            VoiceModuleText.Text = "◌  Voice • standby";
            Respond("Voice control stopped.", false);
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
            if (voiceFactory == null) voiceFactory = await Task.Run(() => WhisperFactory.FromPath(ModelFile));
            if (WaveIn.DeviceCount == 0)
                throw new InvalidOperationException("No microphone was found. Connect a microphone and enable Windows microphone access.");
            if (micDeviceNumber >= WaveIn.DeviceCount || micDeviceNumber < 0) micDeviceNumber = 0;
            listening = true;
            ListenStatus.Text = "● Listening";
            CoreState.Text = "MADI AI • LISTENING";
            MicButton.Content = "■";
            StartVoiceButton.Content = "STOP";
            VoiceModuleText.Text = "●  Voice • listening";
            Respond("Listening. Speak your command.", false);
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
            VoiceModuleText.Text = "◌  Voice • check microphone";
            Respond("Voice setup failed: " + ex.Message, false);
        }
        finally
        {
            voicePreparing = false;
            voiceSetupCancellation?.Dispose();
            voiceSetupCancellation = null;
            if (!listening) { MicButton.Content = "🎙"; StartVoiceButton.Content = "START"; }
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
        if (!listening || processingAudio || microphone != null) return;
        currentRecording = Path.Combine(Path.GetTempPath(), "madi-voice-" + Guid.NewGuid().ToString("N") + ".wav");
        captureStarted = DateTime.UtcNow;
        firstSpeech = DateTime.MinValue;
        lastSpeech = DateTime.MinValue;
        speechFrames = 0;
        Interlocked.Exchange(ref stopRequested, 0);
        microphone = new WaveInEvent { DeviceNumber = micDeviceNumber, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
        waveWriter = new WaveFileWriter(currentRecording, microphone.WaveFormat);
        microphone.DataAvailable += (_, e) =>
        {
            waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            if (Interlocked.CompareExchange(ref stopRequested, 0, 0) != 0) return;
            // Detect speech and end a command after a short silence instead of
            // making the user wait for a fixed seven-second recording.
            long energy = 0;
            int samples = e.BytesRecorded / 2;
            for (int i = 0; i < e.BytesRecorded - 1; i += 2)
            {
                short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                energy += (long)sample * sample;
            }
            double rms = samples > 0 ? Math.Sqrt((double)energy / samples) / 32768.0 : 0;
            var now = DateTime.UtcNow;
            if (rms > 0.014)
            {
                speechFrames++;
                if (speechFrames >= 2)
                {
                    if (firstSpeech == DateTime.MinValue) firstSpeech = now;
                    lastSpeech = now;
                }
            }
            else if (firstSpeech == DateTime.MinValue) speechFrames = 0;
            if (firstSpeech != DateTime.MinValue &&
                (now - firstSpeech).TotalMilliseconds >= 150 &&
                (now - lastSpeech).TotalMilliseconds >= 550)
            {
                if (Interlocked.Exchange(ref stopRequested, 1) == 0)
                    Dispatcher.BeginInvoke(new Action(() => { try { microphone?.StopRecording(); } catch { } }));
            }
        };
        microphone.RecordingStopped += (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                voiceChunk.Stop();
                var file = currentRecording;
                try { waveWriter?.Dispose(); microphone?.Dispose(); } catch { }
                waveWriter = null; microphone = null;
                if (file == null) return;
                if (!listening || firstSpeech == DateTime.MinValue)
                {
                    TryDelete(file);
                    if (listening) StartRecording();
                    return;
                }
                processingAudio = true;
                try { await TranscribeAsync(file); }
                catch (Exception ex) { Respond("Voice recognition error: " + ex.Message, false); }
                finally
                {
                    processingAudio = false;
                    TryDelete(file);
                    // Don't record the assistant's own spoken response.
                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (listening && (chatPending || speaker.State == SynthesizerState.Speaking) && DateTime.UtcNow < deadline)
                        await Task.Delay(100);
                    if (listening) StartRecording();
                }
            }));
        };
        microphone.StartRecording();
        voiceChunk.Start();
    }
    private async Task TranscribeAsync(string wavPath)
    {
        ListenStatus.Text = "● Understanding";
        CoreState.Text = "MADI AI • UNDERSTANDING";
        var selected = LanguagePicker.SelectedIndex;
        var language = selected == 1 ? "en" : selected == 2 ? "ur" : selected == 3 ? "hi" : "auto";
        var factory = voiceFactory ?? throw new InvalidOperationException("Speech engine not initialized.");
        var recognized = await Task.Run(async () =>
        {
            using var processor = factory.CreateBuilder().WithLanguage(language).Build();
            using var stream = File.OpenRead(wavPath);
            var transcript = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(stream))
                transcript.Append(segment.Text).Append(' ');
            return transcript.ToString().Trim();
        });
        if (!string.IsNullOrWhiteSpace(recognized))
        {
            StatusText.Text = "Heard: " + recognized;
            Execute(recognized);
        }
        ListenStatus.Text = listening ? "● Listening" : "● Standby";
        CoreState.Text = listening ? "MADI AI • LISTENING" : "MADI AI • ONLINE";
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private void Browser_Click(object sender, RoutedEventArgs e) => Execute("open google");
    private void Youtube_Click(object sender, RoutedEventArgs e) => Execute("open youtube");
    private void Whatsapp_Click(object sender, RoutedEventArgs e) => Execute("open whatsapp");
    private void Ebay_Click(object sender, RoutedEventArgs e) => Execute("open ebay");
    private void Ali_Click(object sender, RoutedEventArgs e) => Execute("open aliexpress");
    private void Canva_Click(object sender, RoutedEventArgs e) => Execute("open canva");
    private void Mt5_Click(object sender, RoutedEventArgs e) => Execute("open mt5");
    private void Chat_Click(object sender, RoutedEventArgs e) { Respond("Ask Madi AI a question in the command bar. Local AI chat uses Ollama if installed.", false); CommandBox.Focus(); }
    private void ChatGpt_Click(object sender, RoutedEventArgs e) => Execute("open chatgpt");
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
    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!unlocked) return;
        var installedModels = await GetInstalledModelsAsync();
        var window = new Window
        {
            Title = "Madi AI • Settings", Width = 600, Height = 690,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 18, 39)),
            Foreground = System.Windows.Media.Brushes.White
        };
        var panel = new StackPanel { Margin = new Thickness(25) };
        panel.Children.Add(new TextBlock { Text = "MADI AI • AUDIO & PRIVACY", FontSize = 21, FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.Cyan, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Microphone input device", FontSize = 15, Margin = new Thickness(0, 0, 0, 7) });
        var devices = new List<string>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
            devices.Add(WaveIn.GetCapabilities(i).ProductName);
        if (devices.Count == 0) devices.Add("No microphone detected");
        var picker = new System.Windows.Controls.ComboBox
        {
            Height = 40, Width = 475, ItemsSource = devices, SelectedIndex = Math.Clamp(micDeviceNumber, 0, devices.Count - 1),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 28, 53)),
            BorderBrush = System.Windows.Media.Brushes.DeepSkyBlue
        };
        if (TryFindResource("DarkLanguageCombo") is Style comboStyle) picker.Style = comboStyle;
        panel.Children.Add(picker);
        panel.Children.Add(new TextBlock { Text = "Speech recognition: English, Urdu, Hindi or Auto (top-right selector).",
            FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 15, 0, 8) });
        var installed = speaker.GetInstalledVoices().Where(v => v.Enabled)
            .Select(v => v.VoiceInfo.Culture.Name).Distinct().ToList();
        panel.Children.Add(new TextBlock { Text = "Windows spoken voice languages: " + (installed.Count == 0 ? "none" : string.Join(", ", installed)),
            FontSize = 12, Foreground = System.Windows.Media.Brushes.LightSkyBlue, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Secret code: salted hash in your local Windows profile. No code is stored in GitHub.",
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 15) });
        var save = new System.Windows.Controls.Button { Content = "SAVE MICROPHONE SELECTION", Height = 43,
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(28, 91, 161)) };
        save.Click += (_, _) =>
        {
            if (WaveIn.DeviceCount == 0) { Respond("Connect a microphone first.", false); return; }
            micDeviceNumber = picker.SelectedIndex;
            File.WriteAllText(Path.Combine(profile, "settings.json"), JsonSerializer.Serialize(new { microphone = micDeviceNumber }));
            Respond("Microphone saved. Stop and restart voice listening to use the selected device.", false);
            window.Close();
        };
        panel.Children.Add(save);
        var micTest = new System.Windows.Controls.Button { Content = "TEST MICROPHONE (3 SECONDS)", Height = 39, Margin = new Thickness(0, 8, 0, 0),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 65, 105)) };
        micTest.Click += async (_, _) =>
        {
            if (listening || voicePreparing) { Respond("Stop voice listening before testing the microphone.", false); return; }
            if (WaveIn.DeviceCount == 0) { Respond("No microphone detected.", false); return; }
            micTest.IsEnabled = false;
            micTest.Content = "SPEAK NOW — TESTING INPUT";
            try
            {
                double peak = 0;
                using var test = new WaveInEvent { DeviceNumber = picker.SelectedIndex,
                    WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100 };
                test.DataAvailable += (_, args) =>
                {
                    double sum = 0;
                    var count = args.BytesRecorded / 2;
                    for (int i = 0; i + 1 < args.BytesRecorded; i += 2)
                    {
                        var sample = (short)(args.Buffer[i] | (args.Buffer[i + 1] << 8));
                        sum += (double)sample * sample;
                    }
                    var level = count > 0 ? Math.Sqrt(sum / count) / 32768.0 : 0;
                    peak = Math.Max(peak, level);
                };
                test.StartRecording();
                await Task.Delay(3000);
                test.StopRecording();
                Respond(peak > 0.014
                    ? $"Microphone input detected. Peak level: {peak:P1}. Try voice commands now."
                    : $"Microphone signal too low ({peak:P1}). Check the selected device, microphone permissions and input volume.", false);
            }
            catch (Exception ex) { Respond("Microphone test failed: " + ex.Message, false); }
            finally { micTest.IsEnabled = true; micTest.Content = "TEST MICROPHONE (3 SECONDS)"; }
        };
        panel.Children.Add(micTest);
        panel.Children.Add(new TextBlock { Text = "Installed Ollama models (no download required)", Margin = new Thickness(0, 12, 0, 6), FontSize = 14 });
        var modelPicker = new System.Windows.Controls.ComboBox
        {
            Height = 36, ItemsSource = installedModels,
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(8, 28, 53))
        };
        if (TryFindResource("DarkLanguageCombo") is Style modelStyle) modelPicker.Style = modelStyle;
        modelPicker.SelectedItem = installedModels.FirstOrDefault(m => m.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
            ?? installedModels.FirstOrDefault(m => m.Equals("llama3.2:latest", StringComparison.OrdinalIgnoreCase))
            ?? installedModels.FirstOrDefault();
        modelPicker.SelectionChanged += (_, _) =>
        {
            if (modelPicker.SelectedItem is not string name) return;
            selectedModel = name;
            try { File.WriteAllText(SelectedModelFile, name); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            LocalAiModuleText.Text = "●  Local AI • " + name;
        };
        panel.Children.Add(modelPicker);
        panel.Children.Add(new TextBlock { Text = installedModels.Count > 0
            ? "Model detected. Select it above; there is no need to download it again."
            : "No local model detected. Ensure Ollama is running on port 11434.",
            Foreground = System.Windows.Media.Brushes.LightSkyBlue, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var local = new System.Windows.Controls.Button { Content = "CHECK INSTALLED OLLAMA MODELS", Height = 43, Margin = new Thickness(0, 10, 0, 0),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 40, 134)) };
        local.Click += async (_, _) =>
        {
            var detected = await GetInstalledModelsAsync();
            if (detected.Count > 0)
            {
                modelPicker.ItemsSource = detected;
                modelPicker.SelectedItem = detected.FirstOrDefault(m => m.Equals(selectedModel, StringComparison.OrdinalIgnoreCase))
                    ?? detected.FirstOrDefault(m => m.Equals("llama3.2:latest", StringComparison.OrdinalIgnoreCase))
                    ?? detected[0];
                Respond("Ollama is ready. Installed model: " + modelPicker.SelectedItem + ". No download needed.", false);
                return;
            }
            if (FindOllama() == null)
            {
                Respond("Ollama is not installed. Opening its official installer page.", false);
                Open("https://ollama.com/download/windows");
                return;
            }
            Respond("Ollama is installed but its local service or models are unavailable. Start Ollama, then use 'ollama list' to verify models. Nothing was downloaded.", false);
        };
        panel.Children.Add(local);
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
    }
}
