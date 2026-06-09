using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

public class IndicatorEngine
{
    private const int RsiPeriod = 14;

    /// <summary>
    /// Computes RSI using Wilder's smoothing method.
    /// Returns the RSI value for the most recent (last) candle.
    /// </summary>
    public decimal ComputeRsi(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < RsiPeriod + 1)
            throw new ArgumentException($"Need at least {RsiPeriod + 1} candles to compute RSI.");

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            var diff = candles[i].Close - candles[i - 1].Close;
            gains.Add(diff > 0 ? diff : 0);
            losses.Add(diff < 0 ? Math.Abs(diff) : 0);
        }

        // Initial averages over first 14 periods
        decimal avgGain = gains.Take(RsiPeriod).Average();
        decimal avgLoss = losses.Take(RsiPeriod).Average();

        // Wilder's smoothing for remaining periods
        for (int i = RsiPeriod; i < gains.Count; i++)
        {
            avgGain = (avgGain * (RsiPeriod - 1) + gains[i]) / RsiPeriod;
            avgLoss = (avgLoss * (RsiPeriod - 1) + losses[i]) / RsiPeriod;
        }

        if (avgLoss == 0) return 100;
        var rs = avgGain / avgLoss;
        return Math.Round(100 - (100 / (1 + rs)), 2);
    }

    /// <summary>
    /// Computes standard floor pivot points from the previous candle (yesterday or last week).
    /// </summary>
    public PivotLevels ComputePivots(Candle previousCandle)
    {
        var h = previousCandle.High;
        var l = previousCandle.Low;
        var c = previousCandle.Close;

        var pivot = (h + l + c) / 3m;
        var r1 = (2 * pivot) - l;
        var s1 = (2 * pivot) - h;
        var r2 = pivot + (h - l);
        var s2 = pivot - (h - l);
        var r3 = h + 2 * (pivot - l);
        var s3 = l - 2 * (h - pivot);

        return new PivotLevels(
            Pivot: Math.Round(pivot, 5),
            R1: Math.Round(r1, 5), R2: Math.Round(r2, 5), R3: Math.Round(r3, 5),
            S1: Math.Round(s1, 5), S2: Math.Round(s2, 5), S3: Math.Round(s3, 5)
        );
    }

    /// <summary>
    /// Checks whether the candle's intrabar range (High/Low) touched or crossed
    /// the weekly S2 or S3 levels (for a BUY setup).
    /// Returns the level touched and its label, or null if none touched.
    /// </summary>
    public (decimal Level, string Label)? CheckWeeklyBuySrTouch(Candle candle, PivotLevels weeklyPivots)
    {
        // Check S3 first (deeper support — stronger reversal signal)
        if (candle.Low <= weeklyPivots.S3 && candle.High >= weeklyPivots.S3)
            return (weeklyPivots.S3, "Weekly S3");

        if (candle.Low <= weeklyPivots.S2 && candle.High >= weeklyPivots.S2)
            return (weeklyPivots.S2, "Weekly S2");

        return null;
    }

    /// <summary>
    /// Checks whether the candle's intrabar range touched or crossed
    /// weekly R2 or R3 (for a SELL setup).
    /// </summary>
    public (decimal Level, string Label)? CheckWeeklySellSrTouch(Candle candle, PivotLevels weeklyPivots)
    {
        if (candle.High >= weeklyPivots.R3 && candle.Low <= weeklyPivots.R3)
            return (weeklyPivots.R3, "Weekly R3");

        if (candle.High >= weeklyPivots.R2 && candle.Low <= weeklyPivots.R2)
            return (weeklyPivots.R2, "Weekly R2");

        return null;
    }
}
