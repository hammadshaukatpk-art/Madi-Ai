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
    private static string Model => Path.Combine(PiperDirectory, "voices", "en_US-lessac-medium.onnx");

    public static bool IsAvailable => File.Exists(Executable) && File.Exists(Model) &&
                                      File.Exists(Model + ".json");

    public static async Task<bool> SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(text)) return false;
        var wav = Path.Combine(Path.GetTempPath(), "madi-piper-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo(Executable)
                {
                    WorkingDirectory = PiperDirectory,
                    Arguments = "--model \"" + Model + "\" --output_file \"" + wav + "\"",
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            })
            {
                process.Start();
                using var cancellation = cancellationToken.Register(() =>
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                    catch { }
                });
                await process.StandardInput.WriteLineAsync(text.AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await process.WaitForExitAsync(cancellationToken);
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
