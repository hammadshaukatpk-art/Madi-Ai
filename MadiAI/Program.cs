using System;
using System.Windows;
namespace MadiAI;
internal static class Program
{
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
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "madi-voice-smoke.log"),
                    "Whisper multilingual model and native runtime initialized successfully");
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
            win.Loaded += (_, _) => System.IO.File.AppendAllText(log, "Main window loaded" + Environment.NewLine);
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
