using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Evaluates the Reversal Strategy rules in sequence:
///   1. RSI below 30 (oversold) for BUY  /  RSI above 70 (overbought) for SELL
///   2. Price is on the correct side of the daily pivot point
///   3. Current daily candle has touched or crossed Weekly S2/S3 (buy) or R2/R3 (sell) intrabar
///   => All conditions met at daily close => signal is flagged
/// </summary>
public class ReversalStrategyEngine(IndicatorEngine indicators, ILogger<ReversalStrategyEngine> logger)
{
    private const decimal RsiBuyThreshold  = 30m;
    private const decimal RsiSellThreshold = 70m;

    public async Task<SignalResult> EvaluateAsync(
        string symbol,
        List<Candle> dailyCandles,
        List<Candle> weeklyCandles)
    {
        if (dailyCandles.Count < 16)
            throw new ArgumentException("Need at least 16 daily candles.");
        if (weeklyCandles.Count < 2)
            throw new ArgumentException("Need at least 2 weekly candles.");

        var latestDaily  = dailyCandles[^1];         // today's (or last close) candle
        var prevDaily    = dailyCandles[^2];          // previous daily candle for daily pivots
        var prevWeekly   = weeklyCandles[^2];         // last completed week for weekly pivots

        // --- Compute indicators ---
        var rsi          = indicators.ComputeRsi(dailyCandles);
        var dailyPivots  = indicators.ComputePivots(prevDaily);
        var weeklyPivots = indicators.ComputePivots(prevWeekly);

        logger.LogInformation("{Symbol} | Close={Close} RSI={Rsi} DailyPivot={Pivot} WeeklyS2={S2} WeeklyS3={S3}",
            symbol, latestDaily.Close, rsi, dailyPivots.Pivot, weeklyPivots.S2, weeklyPivots.S3);

        // --- Rule 1: RSI condition ---
        bool rsiBuy  = rsi <= RsiBuyThreshold;
        bool rsiSell = rsi >= RsiSellThreshold;
        bool rsiConditionMet = rsiBuy || rsiSell;

        // --- Rule 2: Pivot point alignment ---
        // BUY: price below daily pivot (bearish pressure => looking for reversal UP)
        // SELL: price above daily pivot (bullish pressure => looking for reversal DOWN)
        bool pivotBuyAlignment  = rsiBuy  && latestDaily.Close < dailyPivots.Pivot;
        bool pivotSellAlignment = rsiSell && latestDaily.Close > dailyPivots.Pivot;
        bool pivotAlignmentMet  = pivotBuyAlignment || pivotSellAlignment;

        // --- Rule 3: Weekly S/R intrabar touch ---
        (decimal Level, string Label)? srTouch = null;
        if (pivotBuyAlignment)
            srTouch = indicators.CheckWeeklyBuySrTouch(latestDaily, weeklyPivots);
        else if (pivotSellAlignment)
            srTouch = indicators.CheckWeeklySellSrTouch(latestDaily, weeklyPivots);

        bool weeklySrTouched = srTouch.HasValue;

        // --- Final signal ---
        SignalDirection signal = SignalDirection.None;
        if (rsiBuy  && pivotBuyAlignment  && weeklySrTouched) signal = SignalDirection.Buy;
        if (rsiSell && pivotSellAlignment && weeklySrTouched) signal = SignalDirection.Sell;

        return new SignalResult(
            Symbol:                   symbol,
            EvaluatedAt:              latestDaily.Timestamp,
            CurrentPrice:             latestDaily.Close,
            Rsi:                      rsi,
            DailyPivots:              dailyPivots,
            WeeklyPivots:             weeklyPivots,
            RsiConditionMet:          rsiConditionMet,
            PivotAlignmentMet:        pivotAlignmentMet,
            WeeklySrTouchedIntrabar:  weeklySrTouched,
            TouchedWeeklyLevel:       srTouch?.Level,
            TouchedLevelLabel:        srTouch?.Label ?? "None",
            Signal:                   signal,
            ClaudeNarrative:          null   // filled in by ClaudeExplainerService
        );
    }
}
