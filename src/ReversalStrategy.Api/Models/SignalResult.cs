namespace ReversalStrategy.Api.Models;

public enum SignalDirection { None, Short }

public record SignalResult(
    string Symbol,
    DateTime EvaluatedAt,

    // Price
    decimal CurrentPrice,
    bool    DailyCandleBearish,      // Rule 4: latest daily candle close < open

    // Rule 1 — RSI
    decimal Rsi,
    decimal PreviousRsi,
    bool    RsiBreakingUpperLimit,   // RSI crossed above 70 on daily

    // Rule 2 — Pivot resistance touch + bearish rejection
    PivotLevels DailyPivots,
    decimal?    TouchedPivotLevel,
    string      TouchedPivotLabel,
    bool        PivotTouchWithRejection,  // touched R-level intrabar AND closed below it (bearish)

    // Rule 3 — Weekly level tests
    PivotLevels WeeklyPivots,
    int         WeeklyTestCount,          // how many of last 3-5 weekly candles tested the level
    bool        WeeklyTestedMultipleTimes, // >= 2 tests

    // Final
    SignalDirection Signal,
    string?         ClaudeNarrative
);
