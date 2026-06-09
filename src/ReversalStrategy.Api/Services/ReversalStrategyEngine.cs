using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Evaluates the SHORT Reversal Strategy in 4 sequential steps (all on daily candles unless noted):
///
///   Rule 1 — RSI(14) daily crosses above 70 (overbought breakout)
///   Rule 2 — Price touches a daily pivot resistance (R1/R2/R3) intrabar and closes below it
///             with a bearish candle (rejection/shooting-star pattern)
///   Rule 3 — Switch to WEEKLY candles: the same resistance level has been tested
///             at least 2 times within the last 3–5 weeks
///   Rule 4 — Switch back to DAILY: the last completed daily candle is bearish (close < open)
///
///   => All 4 rules met  →  Signal: SHORT
/// </summary>
public class ReversalStrategyEngine(IndicatorEngine indicators, ILogger<ReversalStrategyEngine> logger)
{
    private const int MinWeeklyTests = 2;
    private const int WeeklyLookback = 5;

    public Task<SignalResult> EvaluateAsync(
        string symbol,
        List<Candle> dailyCandles,
        List<Candle> weeklyCandles)
    {
        if (dailyCandles.Count < 20)
            throw new ArgumentException($"Need at least 20 daily candles for {symbol}.");
        if (weeklyCandles.Count < WeeklyLookback + 1)
            throw new ArgumentException($"Need at least {WeeklyLookback + 1} weekly candles for {symbol}.");

        var latestDaily = dailyCandles[^1];   // last completed daily candle
        var prevDaily   = dailyCandles[^2];   // the one before — used for daily pivot calculation

        // ── Compute daily pivots from the previous completed daily candle ──────────
        var dailyPivots  = indicators.ComputePivots(prevDaily);

        // ── Compute weekly pivots from the last completed weekly candle ───────────
        // weeklyCandles[^1] may be the current (still open) week — use [^2] for completed
        var prevWeekly   = weeklyCandles[^2];
        var weeklyPivots = indicators.ComputePivots(prevWeekly);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // RULE 1 — RSI(14) breaks above 70 on daily
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var (currentRsi, previousRsi) = indicators.ComputeRsi(dailyCandles);
        bool rule1 = indicators.IsRsiBreakingUpperLimit(currentRsi, previousRsi);

        logger.LogInformation("{Symbol} | Rule1 RSI Break: {Rule1} (prev={Prev} curr={Curr})",
            symbol, rule1, previousRsi, currentRsi);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // RULE 2 — Price touches daily pivot resistance + bearish rejection
        // Only evaluate if Rule 1 is met (save computation on non-candidates)
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        (decimal Level, string Label)? pivotTouch = null;
        bool rule2 = false;

        if (rule1)
        {
            pivotTouch = indicators.CheckPivotResistanceRejection(latestDaily, dailyPivots);
            rule2 = pivotTouch.HasValue;

            logger.LogInformation("{Symbol} | Rule2 Pivot Rejection: {Rule2} ({Label} @ {Level})",
                symbol, rule2, pivotTouch?.Label ?? "—", pivotTouch?.Level.ToString() ?? "—");
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // RULE 3 — Weekly candles: resistance tested 2+ times in 3–5 weeks
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        int  weeklyTestCount = 0;
        bool rule3           = false;

        if (rule2 && pivotTouch.HasValue)
        {
            weeklyTestCount = indicators.CountWeeklyLevelTests(
                weeklyCandles, pivotTouch.Value.Level, WeeklyLookback);
            rule3 = weeklyTestCount >= MinWeeklyTests;

            logger.LogInformation("{Symbol} | Rule3 Weekly Tests: {Count} (need {Min}) → {Rule3}",
                symbol, weeklyTestCount, MinWeeklyTests, rule3);
        }

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // RULE 4 — Latest completed daily candle is bearish
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        bool rule4 = rule3 && indicators.IsBearishCandle(latestDaily);

        logger.LogInformation("{Symbol} | Rule4 Bearish Daily: {Rule4} (O={O} C={C})",
            symbol, rule4, latestDaily.Open, latestDaily.Close);

        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        // SIGNAL
        // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
        var signal = (rule1 && rule2 && rule3 && rule4)
            ? SignalDirection.Short
            : SignalDirection.None;

        logger.LogInformation("{Symbol} | *** SIGNAL: {Signal} ***", symbol, signal);

        return Task.FromResult(new SignalResult(
            Symbol:                    symbol,
            EvaluatedAt:               latestDaily.Timestamp,
            CurrentPrice:              latestDaily.Close,
            DailyCandleBearish:        rule4,
            Rsi:                       currentRsi,
            PreviousRsi:               previousRsi,
            RsiBreakingUpperLimit:     rule1,
            DailyPivots:               dailyPivots,
            TouchedPivotLevel:         pivotTouch?.Level,
            TouchedPivotLabel:         pivotTouch?.Label ?? "None",
            PivotTouchWithRejection:   rule2,
            WeeklyPivots:              weeklyPivots,
            WeeklyTestCount:           weeklyTestCount,
            WeeklyTestedMultipleTimes: rule3,
            Signal:                    signal,
            ClaudeNarrative:           null   // filled by ClaudeExplainerService
        ));
    }
}
