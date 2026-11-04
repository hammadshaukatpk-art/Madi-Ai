using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MadiAI;

/// <summary>
/// Offline Sherpa-ONNX TTS for Indic scripts. Uses pre-bundled, locally stored
/// VITS models. No internet connection, account or external API is needed.
/// </summary>
internal static class IndicVoice
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "sherpa");
    private static string Exe => Path.Combine(Root, "sherpa-onnx-offline-tts.exe");
    private static string Language(string text, string? selected) =>
        Regex.IsMatch(text, @"[\u0600-\u06FF]") ? "ur" :
        Regex.IsMatch(text, @"[\u0900-\u097F]") ? "hi" :
        selected is "hi" or "ur" ? selected : "";

    private static string Folder(string language) => Path.Combine(Root, "voices",
        language == "hi" ? "vits-piper-hi_IN-priyamvada-medium" : "vits-piper-ur_PK-fasih-medium");

    private static string Name(string language) =>
        language == "hi" ? "hi_IN-priyamvada-medium.onnx" : "ur_PK-fasih-medium.onnx";

    internal static bool IsAvailable(string text, string? selected = null)
    {
        var language = Language(text, selected);
        if (language is not ("hi" or "ur")) return false;
        var folder = Folder(language);
        return File.Exists(Exe) && File.Exists(Path.Combine(folder, Name(language))) &&
            File.Exists(Path.Combine(folder, "tokens.txt")) &&
            Directory.Exists(Path.Combine(folder, "espeak-ng-data"));
    }

    internal static async Task<bool> SpeakAsync(string text, CancellationToken cancellationToken, string? selected = null)
    {
        if (!IsAvailable(text, selected) || string.IsNullOrWhiteSpace(text)) return false;
        var language = Language(text, selected);
        var folder = Folder(language);
        var outputFile = Path.Combine(Path.GetTempPath(), "madi-indic-" + Guid.NewGuid().ToString("N") + ".wav");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var start = new ProcessStartInfo(Exe)
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--vits-model=" + Path.Combine(folder, Name(language)));
            start.ArgumentList.Add("--vits-tokens=" + Path.Combine(folder, "tokens.txt"));
            start.ArgumentList.Add("--vits-data-dir=" + Path.Combine(folder, "espeak-ng-data"));
            start.ArgumentList.Add("--output-filename=" + outputFile);
            start.ArgumentList.Add(text);
            using (var process = Process.Start(start))
            {
                if (process == null) return false;
                using var cancel = timeout.Token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                });
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0 || !File.Exists(outputFile) || new FileInfo(outputFile).Length < 1000)
                    return false;
            }
            using var reader = new AudioFileReader(outputFile);
            using var playback = new WaveOutEvent();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            playback.PlaybackStopped += (_, e) => done.TrySetResult(e.Exception == null);
            playback.Init(reader);
            using var stop = cancellationToken.Register(() => { try { playback.Stop(); } catch { } });
            playback.Play();
            return await done.Task.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally { try { File.Delete(outputFile); } catch { } }
    }
}
