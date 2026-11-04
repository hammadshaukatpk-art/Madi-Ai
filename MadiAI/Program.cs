using System;
using System.Windows;
namespace MadiAI;
internal static class Program
{
    private static async System.Threading.Tasks.Task<string> TranscribeSmokeSample(
        Whisper.net.WhisperProcessor processor, string path)
    {
        using var stream = System.IO.File.OpenRead(path);
        var text = new System.Text.StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream))
            text.Append(segment.Text).Append(' ');
        return text.ToString().Trim();
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--smoke-routines", StringComparer.OrdinalIgnoreCase))
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-routines-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                if (!WindowsTextAutomation.HasValidNativeLayout)
                    throw new InvalidOperationException("Win64 SendInput structure layout is invalid.");
                if (!WindowsTextAutomation.IsProtectedWindow("terminal64", "EURUSD") ||
                    !WindowsTextAutomation.IsProtectedWindow("other", "MetaTrader 5 - Demo") ||
                    !WindowsTextAutomation.IsProtectedWindow("MadiAI", "Madi AI") ||
                    WindowsTextAutomation.IsProtectedWindow("notepad", "Notes"))
                    throw new InvalidOperationException("Protected desktop app safeguards failed.");
                var routines = new LocalRoutines(folder);
                if (!routines.Save("morning", "open google; open notepad").StartsWith("Saved"))
                    throw new InvalidOperationException("Routine creation failed.");
                if (routines.Save("unsafe", "delete files; shutdown computer").StartsWith("Saved"))
                    throw new InvalidOperationException("Unsafe routine was accepted.");
                var restored = new LocalRoutines(folder);
                if (restored.Get("morning")?.Length != 2)
                    throw new InvalidOperationException("Routine persistence failed.");
                if (!restored.Delete("morning") || new LocalRoutines(folder).Get("morning") != null)
                    throw new InvalidOperationException("Routine deletion failed.");
            }
            catch (Exception ex) { System.Console.Error.WriteLine(ex); Environment.ExitCode = 10; }
            finally { try { System.IO.Directory.Delete(folder, true); } catch { } }
            return;
        }
        if (args.Contains("--smoke-mt5-bridge", StringComparer.OrdinalIgnoreCase))
        {
            var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-mt5-test-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                System.IO.File.WriteAllText(temp, "updated=" + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") +
                    "\nbroker=DemoBroker\nlogin=12345\ncurrency=USD\nbalance=1000.00\nequity=1010.00\nfree_margin=900.00\nposition=456|EURUSD|BUY|0.10|10.00\n");
                var result = Mt5Bridge.ReadFromPath(temp, out var diagnostic);
                if (result == null || result.Login != "12345" || result.Positions.Length != 1)
                    throw new InvalidOperationException("MT5 bridge parsing failed: " + diagnostic);
                var candles = System.Linq.Enumerable.Range(0, 80).Select(i =>
                {
                    var open = 1.1000 + i * 0.0001;
                    var close = open + (i % 3 == 0 ? -0.00005 : 0.00008);
                    var t = new DateTime(2026, 1, 1).AddHours(i).ToString("yyyy.MM.dd HH:mm:ss");
                    return string.Join("|", t, open.ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                        (Math.Max(open, close) + 0.0001).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                        (Math.Min(open, close) - 0.0001).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
                        close.ToString("F6", System.Globalization.CultureInfo.InvariantCulture), "100");
                }).ToArray();
                var description = Mt5ChartAnalysis.Describe("EURUSD", "H1", candles);
                if (!description.Contains("SMA20") || !description.Contains("SMA50") ||
                    !description.Contains("RSI14") || !description.Contains("ATR14") ||
                    !description.Contains("not trading advice"))
                    throw new InvalidOperationException("MT5 local chart analysis regression.");
                if (Mt5ChartAnalysis.TryParse("2026.01.01 00:00:00|1.1|0.9|1.0|1.1|100", out _))
                    throw new InvalidOperationException("Malformed MT5 candle was accepted.");
                System.IO.File.WriteAllText(temp, "updated=" + DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss") +
                    "\nterminal_connected=0\nbroker=DemoBroker\nlogin=12345\ncurrency=USD\nbalance=1000\nequity=1000\nfree_margin=900\n");
                if (Mt5Bridge.ReadFromPath(temp, out var disconnected) != null ||
                    !disconnected.Contains("disconnected", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Disconnected MT5 broker was incorrectly reported as live.");
                System.IO.File.WriteAllText(temp, "updated=2001.01.01 00:00:00\nbroker=DemoBroker\nlogin=12345\ncurrency=USD\nbalance=1000\nequity=1000\nfree_margin=900\n");
                if (Mt5Bridge.ReadFromPath(temp, out _) != null)
                    throw new InvalidOperationException("Stale MT5 status was accepted.");
            }
            catch (Exception ex) { System.Console.Error.WriteLine(ex); Environment.ExitCode = 9; }
            finally { try { System.IO.File.Delete(temp); } catch { } }
            return;
        }
        if (args.Contains("--smoke-memory", StringComparer.OrdinalIgnoreCase))
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-memory-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                var memory = new LocalMemoryStore(folder);
                if (!memory.TryRemember("I prefer Roman Urdu", out _)) throw new InvalidOperationException("Memory write failed.");
                if (memory.TryRemember("My password is 123456", out _)) throw new InvalidOperationException("Sensitive memory accepted.");
                var restored = new LocalMemoryStore(folder);
                if (!restored.Items.Contains("I prefer Roman Urdu")) throw new InvalidOperationException("Memory persistence failed.");
                if (restored.Forget("Roman Urdu") != 1) throw new InvalidOperationException("Memory deletion failed.");
                if (new LocalMemoryStore(folder).Items.Count != 0) throw new InvalidOperationException("Memory deletion was not persisted.");
            }
            catch (Exception ex) { System.Console.Error.WriteLine(ex); Environment.ExitCode = 8; }
            finally { try { System.IO.Directory.Delete(folder, true); } catch { } }
            return;
        }
        if (args.Contains("--smoke-indic-voice", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var recognitionModel = System.IO.Path.Combine(AppContext.BaseDirectory, "models", "ggml-base.bin");
                if (!System.IO.File.Exists(recognitionModel))
                    throw new System.IO.FileNotFoundException("Multilingual recognition model is missing.", recognitionModel);
                using var recognizer = Whisper.net.WhisperFactory.FromPath(recognitionModel);
                foreach (var sample in new[] { ("hi", "नमस्ते, मैं माडी हूँ।"), ("ur", "السلام علیکم، میں ماڈی ہوں۔") })
                {
                    var wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-indic-smoke-" + sample.Item1 + ".wav");
                    var normalized = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-indic-whisper-" + sample.Item1 + ".wav");
                    try
                    {
                        if (!IndicVoice.SynthesizeToFile(sample.Item2, wav, sample.Item1))
                            throw new InvalidOperationException(sample.Item1 + " offline synthesis failed.");
                        using var audio = new NAudio.Wave.WaveFileReader(wav);
                        if (audio.TotalTime.TotalSeconds < 0.5)
                            throw new InvalidOperationException(sample.Item1 + " synthesis output was too short.");
                        // Sherpa VITS voices output their native sample rate; Whisper
                        // requires 16 kHz mono PCM. Convert before recognition.
                        using (var source = new NAudio.Wave.AudioFileReader(wav))
                        using (var converter = new NAudio.Wave.MediaFoundationResampler(source,
                            new NAudio.Wave.WaveFormat(16000, 16, 1)))
                            NAudio.Wave.WaveFileWriter.CreateWaveFile(normalized, converter);
                        using var processor = recognizer.CreateBuilder().WithLanguage(sample.Item1).Build();
                        var transcript = TranscribeSmokeSample(processor, normalized).GetAwaiter().GetResult();
                        if (string.IsNullOrWhiteSpace(transcript))
                            throw new InvalidOperationException(sample.Item1 + " recognition returned no text.");
                    }
                    finally
                    {
                        try { System.IO.File.Delete(wav); } catch { }
                        try { System.IO.File.Delete(normalized); } catch { }
                    }
                }
            }
            catch (Exception ex) { System.Console.Error.WriteLine(ex); Environment.ExitCode = 12; }
            return;
        }
        if (args.Contains("--smoke-voice", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var model = System.IO.Path.Combine(AppContext.BaseDirectory, "models", "ggml-tiny.bin");
                if (!System.IO.File.Exists(model)) throw new System.IO.FileNotFoundException("Bundled voice model is missing.", model);
                using var factory = Whisper.net.WhisperFactory.FromPath(model);
                using var processor = factory.CreateBuilder().WithLanguage("en").Build();
                var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-voice-smoke.log");
                var details = "Whisper multilingual model and native runtime initialized successfully";
                // End-to-end inference: generate a real 16 kHz voice sample on Windows
                // and transcribe it through the same Whisper processor as the desktop UI.
                using (var voice = new System.Speech.Synthesis.SpeechSynthesizer())
                {
                    if (voice.GetInstalledVoices().Any(v => v.Enabled))
                    {
                        var wav = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-voice-smoke-input.wav");
                        try
                        {
                            voice.SetOutputToWaveFile(wav,
                                new System.Speech.AudioFormat.SpeechAudioFormatInfo(
                                    16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,
                                    System.Speech.AudioFormat.AudioChannel.Mono));
                            voice.Speak("Open calculator");
                            voice.SetOutputToNull();
                            var timer = System.Diagnostics.Stopwatch.StartNew();
                            var recognized = TranscribeSmokeSample(processor, wav).GetAwaiter().GetResult();
                            // The dashboard defaults to automatic language selection.
                            // Test that exact Whisper mode, not just forced English.
                            using var autoProcessor = factory.CreateBuilder().WithLanguage("auto").Build();
                            var recognizedAuto = TranscribeSmokeSample(autoProcessor, wav).GetAwaiter().GetResult();
                            if (string.IsNullOrWhiteSpace(recognizedAuto))
                                throw new InvalidOperationException("Automatic language recognition returned no text.");
                            timer.Stop();
                            details += Environment.NewLine + "Automatic language transcript: " + recognizedAuto;
                            details += Environment.NewLine + "Synthetic spoken command: " + recognized +
                                "; recognition seconds: " + timer.Elapsed.TotalSeconds.ToString("F2");
                            if (string.IsNullOrWhiteSpace(recognized))
                                throw new InvalidOperationException("Speech engine returned no transcription for synthetic spoken command.");
                        }
                        finally { try { System.IO.File.Delete(wav); } catch { } }
                    }
                    else details += Environment.NewLine + "No installed Windows TTS voice; synthetic audio test unavailable.";
                }
                System.IO.File.WriteAllText(log, details);
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-voice-smoke.log"), ex.ToString());
                Environment.ExitCode = 7;
            }
            return;
        }
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (args.Contains("--smoke-ui", StringComparer.OrdinalIgnoreCase))
        {
            var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-app-smoke.log");
            System.IO.File.WriteAllText(log, "Starting Madi AI WPF" + Environment.NewLine);
            var win = new MainWindow();
            win.Loaded += (_, _) =>
            {
                var picker = (System.Windows.Controls.ComboBox?)win.FindName("LanguagePicker");
                if (picker == null) throw new InvalidOperationException("Language dropdown missing.");
                foreach (var element in new[] { "ConversationText", "CommandBox", "MicButton", "CoreState",
                    "Mt5ModuleText", "LocalAiModuleText", "VoiceModuleText", "GateOverlay" })
                    if (win.FindName(element) == null)
                        throw new InvalidOperationException("Dashboard element missing: " + element);
                picker.SelectedIndex = 2;
                System.IO.File.AppendAllText(log, "Main window loaded; language selection: " + picker.SelectedIndex + Environment.NewLine);
                picker.SelectedIndex = 0;
                // Reproduce the user's 1339x698 desktop rather than testing only
                // a large 1440x850 developer window.
                win.Width = 1339;
                win.Height = 698;
                win.UpdateLayout();
                var nav = (System.Windows.Controls.Button?)win.FindName("NavHome");
                if (nav == null || nav.ActualHeight < 30)
                    throw new InvalidOperationException("Dashboard navigation is missing or clipped.");
                var composer = (System.Windows.Controls.TextBox?)win.FindName("CommandBox");
                var microphone = (System.Windows.Controls.Button?)win.FindName("MicButton");
                var panels = (System.Windows.Controls.TabControl?)win.FindName("RightPanelTabs");
                if (composer == null || microphone == null || panels == null ||
                    composer.ActualWidth < 200 || microphone.ActualWidth < 50 || panels.ActualWidth < 280)
                    throw new InvalidOperationException("Dashboard command composer or conversation panel is clipped.");
                System.IO.File.AppendAllText(log,
                    $"Dashboard layout: composer={composer.ActualWidth:F0}, mic={microphone.ActualWidth:F0}, panel={panels.ActualWidth:F0}" + Environment.NewLine);
                // Publish an ACTUAL WPF-rendered dashboard image for review.
                // Never substitute a generative design mockup for the built app.
                if (win.FindName("GateOverlay") is System.Windows.FrameworkElement gate)
                    gate.Visibility = Visibility.Collapsed;
                win.UpdateLayout();
                var width = (int)Math.Ceiling(win.ActualWidth);
                var height = (int)Math.Ceiling(win.ActualHeight);
                if (width > 0 && height > 0)
                {
                    var render = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    render.Render(win);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(render));
                    using var output = System.IO.File.Create(
                        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-actual-ui.png"));
                    png.Save(output);
                }
            };
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                System.IO.File.AppendAllText(log, "Main window visible: " + win.IsVisible + "; loaded: " + win.IsLoaded + Environment.NewLine);
                app.Shutdown(win.IsLoaded ? 0 : 3);
            };
            timer.Start();
            app.Run(win);
        }
        else app.Run(new MainWindow());
    }
}
