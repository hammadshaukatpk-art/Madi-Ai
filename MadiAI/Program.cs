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
                            timer.Stop();
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
                picker.SelectedIndex = 2;
                System.IO.File.AppendAllText(log, "Main window loaded; language selection: " + picker.SelectedIndex + Environment.NewLine);
                picker.SelectedIndex = 0;
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
