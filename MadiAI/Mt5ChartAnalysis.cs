using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MadiAI;

/// <summary>
/// Descriptive indicators computed locally from completed MT5 chart candles.
/// No forecasts, trade signals, broker access, or execution.
/// </summary>
internal static class Mt5ChartAnalysis
{
    internal sealed record Candle(DateTime Time, double Open, double High, double Low, double Close, long Volume);

    internal static bool TryParse(string line, out Candle candle)
    {
        candle = default!;
        var parts = line.Split('|');
        if (parts.Length != 6 ||
            !DateTime.TryParseExact(parts[0], "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var open) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var high) ||
            !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var low) ||
            !double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var close) ||
            !long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var volume) ||
            !double.IsFinite(open) || !double.IsFinite(high) ||
            !double.IsFinite(low) || !double.IsFinite(close) ||
            open <= 0 || close <= 0 || low <= 0 || high < low ||
            high < Math.Max(open, close) || low > Math.Min(open, close) || volume < 0)
            return false;
        candle = new Candle(time, open, high, low, close, volume);
        return true;
    }

    internal static string Describe(string symbol, string timeframe, IEnumerable<string> rawCandles)
    {
        var candles = rawCandles.Select(s => TryParse(s, out var candle) ? candle : null)
            .Where(c => c != null).Cast<Candle>().OrderBy(c => c.Time).ToArray();
        if (candles.Length < 55)
            return "MT5 chart analysis requires at least 55 completed candles. " +
                   "Keep the read-only Madi bridge attached to a chart and wait for updated data.";
        if (candles.Zip(candles.Skip(1)).Any(p => p.First.Time == p.Second.Time))
            return "MT5 returned duplicate candle timestamps. Chart analysis was skipped.";

        var close = candles.Select(c => c.Close).ToArray();
        var last = candles[^1];
        var sma20 = close.TakeLast(20).Average();
        var sma50 = close.TakeLast(50).Average();
        var changes = close.Zip(close.Skip(1), (a, b) => b - a).TakeLast(14).ToArray();
        var gains = changes.Sum(v => Math.Max(0, v)) / 14;
        var losses = changes.Sum(v => Math.Max(0, -v)) / 14;
        var rsi = losses == 0 ? (gains == 0 ? 50 : 100) :
            100 - 100 / (1 + gains / losses);

        var trueRanges = new List<double>();
        for (var i = Math.Max(1, candles.Length - 14); i < candles.Length; i++)
        {
            var c = candles[i];
            var prev = candles[i - 1].Close;
            trueRanges.Add(Math.Max(c.High - c.Low,
                Math.Max(Math.Abs(c.High - prev), Math.Abs(c.Low - prev))));
        }
        var atr14 = trueRanges.Average();
        var digits = last.Close >= 100 ? 2 : last.Close >= 1 ? 4 : 6;
        string P(double value) => value.ToString("F" + digits, CultureInfo.InvariantCulture);
        return $"Read-only MT5 chart statistics for {symbol} {timeframe} " +
               $"(last completed candle {last.Time:yyyy-MM-dd HH:mm} terminal time): " +
               $"Close {P(last.Close)}, SMA20 {P(sma20)}, SMA50 {P(sma50)}, " +
               $"RSI14 {rsi:F1}/100, ATR14 {P(atr14)}. " +
               $"Close is {(last.Close > sma20 ? "above" : last.Close < sma20 ? "below" : "equal to")} " +
               $"the 20-candle average. These are historical measurements, not trading advice or a prediction.";
    }
}
