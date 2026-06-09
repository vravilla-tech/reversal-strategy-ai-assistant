using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Evaluates both LONG and SHORT reversal setups using the same 4-step rule chain.
///
/// SHORT setup (sell signal):
///   Rule 1 — RSI(14) daily crosses above 70 (overbought breakout)
///   Rule 2 — Daily candle wicks into R1/R2/R3 and closes below it (bearish rejection)
///   Rule 3 — Weekly: that resistance tested >= 2 times in last 3–5 weeks
///   Rule 4 — Latest completed daily candle is bearish (close < open)
///   => Signal: SHORT ▼
///
/// LONG setup (buy signal):
///   Rule 1 — RSI(14) daily crosses below 30 (oversold breakout)
///   Rule 2 — Daily candle wicks into S1/S2/S3 and closes above it (bullish bounce)
///   Rule 3 — Weekly: that support tested >= 2 times in last 3–5 weeks
///   Rule 4 — Latest completed daily candle is bullish (close > open)
///   => Signal: LONG ▲
/// </summary>
public class ReversalStrategyEngine(IndicatorEngine indicators, ILogger<ReversalStrategyEngine> logger)
{
    private const int MinWeeklyTests = 2;
    private const int WeeklyLookback = 5;

    /// <summary>
    /// Evaluates both directions and returns whichever signal fires.
    /// If both fire simultaneously (rare), SHORT takes precedence.
    /// </summary>
    public async Task<(SignalResult Short, SignalResult Long)> EvaluateBothAsync(
        string symbol,
        List<Candle> dailyCandles,
        List<Candle> weeklyCandles)
    {
        var shortResult = await EvaluateAsync(symbol, SignalDirection.Short, dailyCandles, weeklyCandles);
        var longResult  = await EvaluateAsync(symbol, SignalDirection.Long,  dailyCandles, weeklyCandles);
        return (shortResult, longResult);
    }

    /// <summary>
    /// Evaluates one direction (Long or Short) and returns a <see cref="SignalResult"/>.
    /// </summary>
    public Task<SignalResult> EvaluateAsync(
        string symbol,
        SignalDirection direction,
        List<Candle> dailyCandles,
        List<Candle> weeklyCandles)
    {
        if (dailyCandles.Count < 20)
            throw new ArgumentException($"Need at least 20 daily candles for {symbol}.");
        if (weeklyCandles.Count < WeeklyLookback + 1)
            throw new ArgumentException($"Need at least {WeeklyLookback + 1} weekly candles for {symbol}.");

        var latestDaily = dailyCandles[^1];
        var prevDaily   = dailyCandles[^2];

        var dailyPivots  = indicators.ComputePivots(prevDaily);
        var weeklyPivots = indicators.ComputePivots(weeklyCandles[^2]);  // last completed week

        var (currRsi, prevRsi) = indicators.ComputeRsi(dailyCandles);

        bool isShort = direction == SignalDirection.Short;

        // ── Rule 1 ──────────────────────────────────────────────────────
        bool rule1 = isShort
            ? indicators.IsRsiBreakingUpperLimit(currRsi, prevRsi)
            : indicators.IsRsiBreakingLowerLimit(currRsi, prevRsi);

        logger.LogInformation("{Symbol} {Dir} | Rule1 RSI: {R1} (prev={P} curr={C})",
            symbol, direction, rule1, prevRsi, currRsi);

        // ── Rule 2 ──────────────────────────────────────────────────────
        (decimal Level, string Label)? pivotTouch = null;
        bool rule2 = false;

        if (rule1)
        {
            pivotTouch = isShort
                ? indicators.CheckResistanceRejection(latestDaily, dailyPivots)
                : indicators.CheckSupportBounce(latestDaily, dailyPivots);
            rule2 = pivotTouch.HasValue;

            logger.LogInformation("{Symbol} {Dir} | Rule2 Pivot: {R2} ({Label})",
                symbol, direction, rule2, pivotTouch?.Label ?? "—");
        }

        // ── Rule 3 ──────────────────────────────────────────────────────
        int  weeklyTestCount = 0;
        bool rule3           = false;

        if (rule2 && pivotTouch.HasValue)
        {
            weeklyTestCount = isShort
                ? indicators.CountWeeklyResistanceTests(weeklyCandles, pivotTouch.Value.Level, WeeklyLookback)
                : indicators.CountWeeklySupportTests   (weeklyCandles, pivotTouch.Value.Level, WeeklyLookback);
            rule3 = weeklyTestCount >= MinWeeklyTests;

            logger.LogInformation("{Symbol} {Dir} | Rule3 Weekly Tests: {Count} → {R3}",
                symbol, direction, weeklyTestCount, rule3);
        }

        // ── Rule 4 ──────────────────────────────────────────────────────
        bool rule4 = rule3 && (isShort
            ? indicators.IsBearishCandle(latestDaily)
            : indicators.IsBullishCandle(latestDaily));

        logger.LogInformation("{Symbol} {Dir} | Rule4 Candle: {R4} (O={O} C={C})",
            symbol, direction, rule4, latestDaily.Open, latestDaily.Close);

        // ── Signal ──────────────────────────────────────────────────────
        var signal = (rule1 && rule2 && rule3 && rule4) ? direction : SignalDirection.None;
        logger.LogInformation("{Symbol} {Dir} | *** SIGNAL: {Signal} ***", symbol, direction, signal);

        return Task.FromResult(new SignalResult(
            Symbol:                    symbol,
            EvaluatedAt:               latestDaily.Timestamp,
            Direction:                 direction,
            CurrentPrice:              latestDaily.Close,
            Rsi:                       currRsi,
            PreviousRsi:               prevRsi,
            RsiBreakingLimit:          rule1,
            DailyPivots:               dailyPivots,
            TouchedPivotLevel:         pivotTouch?.Level,
            TouchedPivotLabel:         pivotTouch?.Label ?? "None",
            PivotTouchWithRejection:   rule2,
            WeeklyPivots:              weeklyPivots,
            WeeklyTestCount:           weeklyTestCount,
            WeeklyTestedMultipleTimes: rule3,
            ConfirmationCandleMet:     rule4,
            Signal:                    signal,
            ClaudeNarrative:           null
        ));
    }
}
