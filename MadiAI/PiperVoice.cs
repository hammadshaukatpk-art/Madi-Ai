using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace MadiAI;

/// <summary>
/// Optional local neural English voice. No cloud calls or paid API.
/// Uses bundled Piper executable/model if present; Windows speech is fallback.
/// </summary>
internal static class PiperVoice
{
    private static string PiperDirectory => Path.Combine(AppContext.BaseDirectory, "piper");
    private static string Executable => Path.Combine(PiperDirectory, "piper.exe");
    private static string ModelFor(string text) => Path.Combine(PiperDirectory, "voices",
        System.Text.RegularExpressions.Regex.IsMatch(text, @"[\u0600-\u06FF]")
            ? "ur_PK-aegis_female-medium.onnx" : "en_US-ljspeech-medium.onnx");

    public static bool IsAvailableFor(string text)
    {
        var model = ModelFor(text);
        return File.Exists(Executable) && File.Exists(model) && File.Exists(model + ".json");
    }

    public static async Task<bool> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsAvailableFor(text) || string.IsNullOrWhiteSpace(text)) return false;
        var model = ModelFor(text);
        var wav = Path.Combine(Path.GetTempPath(), "madi-piper-" + Guid.NewGuid().ToString("N") + ".wav");
        // A corrupted or stalled local synthesizer must never block the
        // assistant's microphone loop indefinitely.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(Executable)
                {
                    WorkingDirectory = PiperDirectory,
                    Arguments = "--model \"" + model + "\" --output_file \"" + wav + "\"",
                    RedirectStandardInput = true,
                    StandardInputEncoding = System.Text.Encoding.UTF8,
                    RedirectStandardError = false, // Avoid a full redirected pipe blocking Piper.
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            })
            {
                process.Start();
                using var cancellation = timeout.Token.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch { }
                });
                await process.StandardInput.WriteLineAsync(text.AsMemory(), timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode != 0 || !File.Exists(wav) || new FileInfo(wav).Length < 1000)
                    return false;
            }
            using var reader = new AudioFileReader(wav);
            using var output = new WaveOutEvent();
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, e) => completed.TrySetResult(e.Exception == null);
            output.Init(reader);
            using var stop = cancellationToken.Register(() => { try { output.Stop(); } catch { } });
            output.Play();
            return await completed.Task.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }
}
