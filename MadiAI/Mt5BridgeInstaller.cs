using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace MadiAI;

/// <summary>
/// Installs source for the read-only Expert Advisor into local MT5 data folders.
/// MT5 itself must compile and attach the EA to a chart; no trade permission needed.
/// </summary>
internal static class Mt5BridgeInstaller
{
    public static async Task<string> InstallAsync()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "MT5", "MadiAI_Bridge.mq5");
        if (!File.Exists(source))
            return "The bundled Madi MT5 bridge source is missing from this installation.";
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MetaQuotes", "Terminal");
        if (!Directory.Exists(root))
            return "No MT5 terminal data folder was found. Open MT5 at least once, then try again.";
        var destinations = Directory.EnumerateDirectories(root)
            .Select(x => Path.Combine(x, "MQL5", "Experts"))
            .Where(Directory.Exists).Take(10).ToArray();
        if (destinations.Length == 0)
            return "No MT5 Experts directory was found. Open your terminal and choose File > Open Data Folder.";
        var message = $"Install Madi AI's READ-ONLY bridge into {destinations.Length} local MT5 terminal folder(s)? " +
                      "This will replace any existing MadiAI_Bridge.mq5 source. It does not trade or enable automated trading.";
        if (MessageBox.Show(message, "Madi AI • Connect MetaTrader 5", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return "MT5 bridge installation cancelled.";

        var installed = 0;
        var compiled = 0;
        var errors = new List<string>();
        foreach (var experts in destinations)
        {
            try
            {
                var destination = Path.Combine(experts, "MadiAI_Bridge.mq5");
                File.Copy(source, destination, true);
                installed++;
                var metaEditor = FindMetaEditor();
                if (metaEditor != null)
                {
                    var log = Path.Combine(Path.GetTempPath(), "madi-mt5-compile-" + Guid.NewGuid().ToString("N") + ".log");
                    using var process = Process.Start(new ProcessStartInfo(metaEditor)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        Arguments = "/compile:\"" + destination + "\" /log:\"" + log + "\""
                    });
                    if (process != null)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        try { await process.WaitForExitAsync(timeout.Token); }
                        catch (OperationCanceledException) { try { process.Kill(); } catch { } }
                        if (File.Exists(Path.ChangeExtension(destination, ".ex5"))) compiled++;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                errors.Add(ex.Message);
            }
        }
        if (installed == 0) return "Could not install MT5 bridge: " + string.Join("; ", errors.Take(2));
        return $"Installed read-only bridge source into {installed} MT5 folder(s); {compiled} compiled EA(s) detected. " +
               "In MetaTrader 5, open Navigator > Expert Advisors, refresh, then attach MadiAI_Bridge to a chart. " +
               "Madi AI can then read account balance, equity and open positions. No trade operations are enabled.";
    }

    private static string? FindMetaEditor()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("terminal64"))
            {
                try
                {
                    var directory = Path.GetDirectoryName(process.MainModule?.FileName);
                    if (directory != null)
                    {
                        var path = Path.Combine(directory, "metaeditor64.exe");
                        if (File.Exists(path)) return path;
                    }
                }
                catch { }
            }
        }
        catch { }
        return null;
    }
}
