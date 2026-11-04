using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MadiAI;

/// <summary>
/// Reads the status written by the optional Madi MT5 Expert Advisor.
/// Never infers broker connectivity from a running process alone.
/// The bridge is read-only and cannot place, change or close orders.
/// </summary>
internal static class Mt5Bridge
{
    private static string StatusPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MetaQuotes", "Terminal", "Common", "Files", "MadiAI", "status.txt");

    internal sealed record Snapshot(
        DateTime Timestamp, string Broker, string Login, string Currency,
        string Balance, string Equity, string FreeMargin, string[] Positions);

    internal static Snapshot? Read(out string diagnostic) => ReadFromPath(StatusPath, out diagnostic);

    internal static Snapshot? ReadFromPath(string path, out string diagnostic)
    {
        diagnostic = "No Madi MT5 bridge detected. Attach MadiAI_Bridge to an MT5 chart to enable live account readout.";
        try
        {
            if (!File.Exists(path)) return null;
            var data = File.ReadAllLines(path);
            if (data.Length < 6 || data.Length > 500)
            {
                diagnostic = "The MT5 bridge status file is incomplete.";
                return null;
            }
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in data)
            {
                var split = line.IndexOf('=');
                if (split > 0 && split < 60)
                    values[line[..split]] = line[(split + 1)..].Trim();
            }
            if (!values.TryGetValue("updated", out var updated) ||
                !DateTime.TryParseExact(updated, "yyyy.MM.dd HH:mm:ss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                diagnostic = "The MT5 bridge status timestamp is invalid.";
                return null;
            }
            if (Math.Abs((DateTime.Now - timestamp).TotalSeconds) > 20)
            {
                diagnostic = "The MT5 bridge has stopped updating. Open the terminal and check its Expert Advisor.";
                return null;
            }
            string Get(string key) => values.TryGetValue(key, out var value) ? value : "unavailable";
            var positions = data.Where(x => x.StartsWith("position=", StringComparison.OrdinalIgnoreCase))
                .Select(x => x["position=".Length..]).Take(40).ToArray();
            diagnostic = "Live read-only MT5 bridge connected.";
            return new Snapshot(timestamp, Get("broker"), Get("login"), Get("currency"),
                Get("balance"), Get("equity"), Get("free_margin"), positions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostic = "Cannot read MT5 status: " + ex.Message;
            return null;
        }
    }

    internal static string RequestChart(string symbol, string timeframe)
    {
        if (Read(out var diagnostic) == null) return diagnostic;
        symbol = symbol.Trim().ToUpperInvariant();
        timeframe = timeframe.Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(symbol, @"^[A-Z0-9._-]{2,20}$"))
            return "Invalid MT5 symbol. Example: EURUSD or XAUUSD.";
        if (!new[] { "M1", "M5", "M15", "M30", "H1", "H4", "D1", "W1", "MN1" }.Contains(timeframe))
            return "Unsupported MT5 timeframe. Use M1, M5, M15, M30, H1, H4, D1, W1 or MN1.";
        try
        {
            var folder = Path.GetDirectoryName(StatusPath)!;
            var command = Path.Combine(folder, "command.txt");
            var temp = Path.Combine(folder, "command.tmp");
            File.WriteAllText(temp, "chart|" + symbol + "|" + timeframe + Environment.NewLine);
            File.Move(temp, command, true);
            return $"Requested MT5 chart {symbol} on {timeframe}. The connected terminal will process the command; this is not yet confirmation that the chart changed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "Could not send MT5 chart request: " + ex.Message;
        }
    }

    internal static string Describe(string topic)
    {
        var snapshot = Read(out var diagnostic);
        if (snapshot == null) return diagnostic;
        if (topic is "balance" or "equity" or "account")
            return $"MT5 account {snapshot.Login} at {snapshot.Broker}. Balance: {snapshot.Balance} {snapshot.Currency}. " +
                   $"Equity: {snapshot.Equity} {snapshot.Currency}. Free margin: {snapshot.FreeMargin} {snapshot.Currency}. " +
                   $"Last updated {snapshot.Timestamp:HH:mm:ss}.";
        if (topic is "positions" or "trades" or "orders")
            return snapshot.Positions.Length == 0 ? "MT5 bridge reports no open positions." :
                "Open MT5 positions (ticket | symbol | side | volume | floating profit):\n" +
                string.Join("\n", snapshot.Positions);
        return $"MT5 bridge is active for account {snapshot.Login} at {snapshot.Broker}, " +
               $"last updated {snapshot.Timestamp:HH:mm:ss}. {snapshot.Positions.Length} open position(s).";
    }
}
