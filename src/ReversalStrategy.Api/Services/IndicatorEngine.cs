using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

public class IndicatorEngine
{
    private const int    RsiPeriod      = 14;
    public  const decimal RsiUpperLimit = 70m;   // SHORT threshold
    public  const decimal RsiLowerLimit = 30m;   // LONG threshold

    // ══════════════════════════════════════════
    // RSI  (Wilder's Smoothing)
    // ══════════════════════════════════════════

    /// <summary>
    /// Computes RSI for the two most recent candles.
    /// Returns (currentRsi, previousRsi).
    /// </summary>
    public (decimal Current, decimal Previous) ComputeRsi(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < RsiPeriod + 2)
            throw new ArgumentException($"Need at least {RsiPeriod + 2} daily candles to compute RSI.");

        var gains  = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            var diff = candles[i].Close - candles[i - 1].Close;
            gains .Add(diff > 0 ? diff          : 0m);
            losses.Add(diff < 0 ? Math.Abs(diff) : 0m);
        }

        decimal avgGain = gains .Take(RsiPeriod).Average();
        decimal avgLoss = losses.Take(RsiPeriod).Average();
        decimal prevRsi = RsiFromAvg(avgGain, avgLoss);

        for (int i = RsiPeriod; i < gains.Count - 1; i++)
        {
            avgGain = (avgGain * (RsiPeriod - 1) + gains[i])  / RsiPeriod;
            avgLoss = (avgLoss * (RsiPeriod - 1) + losses[i]) / RsiPeriod;
            prevRsi = RsiFromAvg(avgGain, avgLoss);
        }

        int last = gains.Count - 1;
        avgGain = (avgGain * (RsiPeriod - 1) + gains[last])  / RsiPeriod;
        avgLoss = (avgLoss * (RsiPeriod - 1) + losses[last]) / RsiPeriod;
        decimal currRsi = RsiFromAvg(avgGain, avgLoss);

        return (Math.Round(currRsi, 2), Math.Round(prevRsi, 2));
    }

    // ── Rule 1 helpers ──────────────────────────────────────────────────

    /// <summary>SHORT Rule 1 — RSI crosses from below 70 to >= 70.</summary>
    public bool IsRsiBreakingUpperLimit(decimal currentRsi, decimal previousRsi)
        => previousRsi < RsiUpperLimit && currentRsi >= RsiUpperLimit;

    /// <summary>LONG Rule 1 — RSI crosses from above 30 to &lt;= 30.</summary>
    public bool IsRsiBreakingLowerLimit(decimal currentRsi, decimal previousRsi)
        => previousRsi > RsiLowerLimit && currentRsi <= RsiLowerLimit;

    private static decimal RsiFromAvg(decimal avgGain, decimal avgLoss)
    {
        if (avgLoss == 0) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    // ══════════════════════════════════════════
    // Pivot Points  (Classic Floor / Standard)
    // ══════════════════════════════════════════

    /// <summary>Computes standard floor pivot levels from a completed candle.</summary>
    public PivotLevels ComputePivots(Candle previousCandle)
    {
        var h = previousCandle.High;
        var l = previousCandle.Low;
        var c = previousCandle.Close;

        var p  = (h + l + c) / 3m;
        var r1 = (2 * p) - l;
        var s1 = (2 * p) - h;
        var r2 = p + (h - l);
        var s2 = p - (h - l);
        var r3 = h  + 2 * (p - l);
        var s3 = l  - 2 * (h - p);

        return new PivotLevels(
            Pivot: R(p),
            R1: R(r1), R2: R(r2), R3: R(r3),
            S1: R(s1), S2: R(s2), S3: R(s3)
        );
    }

    // ══════════════════════════════════════════
    // Rule 2 — Pivot touch + rejection
    // ══════════════════════════════════════════

    /// <summary>
    /// SHORT Rule 2 — candle wicks into a resistance level (R1/R2/R3),
    /// closes below it, body is bearish, upper wick is meaningful (≥30% of range).
    /// Returns the first matching resistance level or null.
    /// </summary>
    public (decimal Level, string Label)? CheckResistanceRejection(Candle candle, PivotLevels pivots)
    {
        foreach (var (level, label) in ResistanceLevels(pivots))
        {
            if (candle.High  >= level              // wick touched resistance
             && candle.Close  < level              // closed below (rejected)
             && candle.Close  < candle.Open        // bearish body
             && UpperWickRatio(candle) >= 0.30m)   // meaningful rejection wick
                return (level, label);
        }
        return null;
    }

    /// <summary>
    /// LONG Rule 2 — candle wicks into a support level (S1/S2/S3),
    /// closes above it, body is bullish, lower wick is meaningful (≥30% of range).
    /// Returns the first matching support level or null.
    /// </summary>
    public (decimal Level, string Label)? CheckSupportBounce(Candle candle, PivotLevels pivots)
    {
        foreach (var (level, label) in SupportLevels(pivots))
        {
            if (candle.Low   <= level              // wick touched support
             && candle.Close  > level              // closed above (bounced)
             && candle.Close  > candle.Open        // bullish body
             && LowerWickRatio(candle) >= 0.30m)   // meaningful bounce wick
                return (level, label);
        }
        return null;
    }

    // ══════════════════════════════════════════
    // Rule 3 — Weekly level test count
    // ══════════════════════════════════════════

    /// <summary>
    /// Counts how many of the last <paramref name="lookbackWeeks"/> completed weekly
    /// candles tested a resistance level (High >= level) for SHORT setups.
    /// </summary>
    public int CountWeeklyResistanceTests(IReadOnlyList<Candle> weeklyCandles,
        decimal level, int lookbackWeeks = 5)
        => CompletedWeeklyCandles(weeklyCandles, lookbackWeeks)
           .Count(c => c.High >= level);

    /// <summary>
    /// Counts how many of the last <paramref name="lookbackWeeks"/> completed weekly
    /// candles tested a support level (Low &lt;= level) for LONG setups.
    /// </summary>
    public int CountWeeklySupportTests(IReadOnlyList<Candle> weeklyCandles,
        decimal level, int lookbackWeeks = 5)
        => CompletedWeeklyCandles(weeklyCandles, lookbackWeeks)
           .Count(c => c.Low <= level);

    // ══════════════════════════════════════════
    // Rule 4 — Confirmation candle
    // ══════════════════════════════════════════

    /// <summary>SHORT Rule 4 — completed daily candle closes bearish (close &lt; open).</summary>
    public bool IsBearishCandle(Candle candle) => candle.Close < candle.Open;

    /// <summary>LONG Rule 4 — completed daily candle closes bullish (close > open).</summary>
    public bool IsBullishCandle(Candle candle) => candle.Close > candle.Open;

    // ══════════════════════════════════════════
    // Private helpers
    // ══════════════════════════════════════════

    private static IEnumerable<(decimal, string)> ResistanceLevels(PivotLevels p) =>
    [
        (p.R3, "Daily R3"),
        (p.R2, "Daily R2"),
        (p.R1, "Daily R1"),
    ];

    private static IEnumerable<(decimal, string)> SupportLevels(PivotLevels p) =>
    [
        (p.S3, "Daily S3"),
        (p.S2, "Daily S2"),
        (p.S1, "Daily S1"),
    ];

    private static IEnumerable<Candle> CompletedWeeklyCandles(
        IReadOnlyList<Candle> candles, int lookback)
        => candles
            .TakeLast(lookback + 1)  // +1 in case the last weekly is still open
            .SkipLast(1)
            .TakeLast(lookback);

    /// <summary>Upper wick as a fraction of the total candle range.</summary>
    public static decimal UpperWickRatio(Candle c)
    {
        var range = c.High - c.Low;
        return range == 0 ? 0 : (c.High - Math.Max(c.Open, c.Close)) / range;
    }

    /// <summary>Lower wick as a fraction of the total candle range.</summary>
    public static decimal LowerWickRatio(Candle c)
    {
        var range = c.High - c.Low;
        return range == 0 ? 0 : (Math.Min(c.Open, c.Close) - c.Low) / range;
    }

    private static decimal R(decimal v) => Math.Round(v, 5);
}
