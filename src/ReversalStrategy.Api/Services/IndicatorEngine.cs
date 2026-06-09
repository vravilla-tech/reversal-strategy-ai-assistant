using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

public class IndicatorEngine
{
    private const int    RsiPeriod        = 14;
    public  const decimal RsiUpperLimit   = 70m;

    // ─────────────────────────────────────────────
    // RSI
    // ─────────────────────────────────────────────

    /// <summary>
    /// Computes RSI values for the last two candles using Wilder's smoothing.
    /// Returns (currentRsi, previousRsi).
    /// </summary>
    public (decimal Current, decimal Previous) ComputeRsi(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < RsiPeriod + 2)
            throw new ArgumentException($"Need at least {RsiPeriod + 2} daily candles to compute RSI.");

        // Compute RSI over entire series; track last two values
        var gains  = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            var diff = candles[i].Close - candles[i - 1].Close;
            gains .Add(diff > 0 ? diff        : 0m);
            losses.Add(diff < 0 ? Math.Abs(diff) : 0m);
        }

        decimal avgGain = gains .Take(RsiPeriod).Average();
        decimal avgLoss = losses.Take(RsiPeriod).Average();

        decimal prevRsiValue = RsiFromAvg(avgGain, avgLoss);

        for (int i = RsiPeriod; i < gains.Count - 1; i++)
        {
            avgGain = (avgGain * (RsiPeriod - 1) + gains[i])  / RsiPeriod;
            avgLoss = (avgLoss * (RsiPeriod - 1) + losses[i]) / RsiPeriod;
            prevRsiValue = RsiFromAvg(avgGain, avgLoss);
        }

        // Final step for current candle
        int last = gains.Count - 1;
        avgGain = (avgGain * (RsiPeriod - 1) + gains[last])  / RsiPeriod;
        avgLoss = (avgLoss * (RsiPeriod - 1) + losses[last]) / RsiPeriod;
        decimal currRsiValue = RsiFromAvg(avgGain, avgLoss);

        return (Math.Round(currRsiValue, 2), Math.Round(prevRsiValue, 2));
    }

    /// <summary>
    /// Rule 1 — RSI is breaking above the upper limit (70).
    /// True when: previous RSI was below 70 AND current RSI >= 70.
    /// </summary>
    public bool IsRsiBreakingUpperLimit(decimal currentRsi, decimal previousRsi)
        => previousRsi < RsiUpperLimit && currentRsi >= RsiUpperLimit;

    private static decimal RsiFromAvg(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    // ─────────────────────────────────────────────
    // Pivot Points  (standard floor / classic method)
    // ─────────────────────────────────────────────

    /// <summary>
    /// Computes standard floor pivot points from the previous completed candle.
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
        var r3 = h  + 2 * (pivot - l);
        var s3 = l  - 2 * (h - pivot);

        return new PivotLevels(
            Pivot: Round(pivot),
            R1: Round(r1), R2: Round(r2), R3: Round(r3),
            S1: Round(s1), S2: Round(s2), S3: Round(s3)
        );
    }

    // ─────────────────────────────────────────────
    // Rule 2 — Pivot resistance touch + bearish rejection
    // ─────────────────────────────────────────────

    /// <summary>
    /// Rule 2 — checks if the daily candle:
    ///   a) touched or breached a pivot resistance level (R1, R2, or R3) intrabar (High >= level), AND
    ///   b) closed BELOW that level (rejected — bearish candle at resistance).
    /// Returns the first resistance level met, or null if none.
    /// </summary>
    public (decimal Level, string Label)? CheckPivotResistanceRejection(Candle candle, PivotLevels pivots)
    {
        // Check from strongest resistance downward: R3 → R2 → R1
        var levels = new[]
        {
            (pivots.R3, "Daily R3"),
            (pivots.R2, "Daily R2"),
            (pivots.R1, "Daily R1"),
        };

        foreach (var (level, label) in levels)
        {
            bool touchedIntrabar  = candle.High  >= level;          // wick reached resistance
            bool closedBelow      = candle.Close  < level;           // rejected — closed below
            bool bearishCandle    = candle.Close  < candle.Open;     // candle body is bearish
            bool meaningfulReject = (candle.High - candle.Close)     // upper wick at least 30% of range
                                    >= 0.3m * (candle.High - candle.Low + 0.00001m);

            if (touchedIntrabar && closedBelow && bearishCandle && meaningfulReject)
                return (level, label);
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // Rule 3 — Weekly level test count
    // ─────────────────────────────────────────────

    /// <summary>
    /// Rule 3 — counts how many weekly candles (in the last 3–5 weeks)
    /// tested a given resistance level (candle High >= level).
    /// </summary>
    public int CountWeeklyLevelTests(IReadOnlyList<Candle> weeklyCandles, decimal resistanceLevel, int lookbackWeeks = 5)
    {
        // Take the last N completed weekly candles (exclude the most recent if it's still open)
        var candles = weeklyCandles
            .TakeLast(lookbackWeeks + 1)   // +1 in case last is current week
            .SkipLast(1)                   // drop current (potentially open) weekly candle
            .TakeLast(lookbackWeeks)
            .ToList();

        return candles.Count(c => c.High >= resistanceLevel);
    }

    // ─────────────────────────────────────────────
    // Rule 4 — Bearish daily candle
    // ─────────────────────────────────────────────

    /// <summary>
    /// Rule 4 — the completed daily candle closed bearish (close strictly below open).
    /// </summary>
    public bool IsBearishCandle(Candle candle) => candle.Close < candle.Open;

    // ─────────────────────────────────────────────
    private static decimal Round(decimal v) => Math.Round(v, 5);
}
