namespace ReversalStrategy.Api.Models;

public enum SignalDirection { None, Long, Short }

/// <summary>
/// Unified result for both LONG and SHORT reversal evaluations.
/// All 4 rule flags are direction-agnostic; the Signal field indicates which fired.
/// </summary>
public record SignalResult(
    string Symbol,
    DateTime EvaluatedAt,
    SignalDirection Direction,   // which direction was evaluated (Long or Short)

    // ── Price ──────────────────────────────────
    decimal CurrentPrice,

    // ── Rule 1 — RSI ───────────────────────────
    decimal Rsi,
    decimal PreviousRsi,
    bool    RsiBreakingLimit,       // SHORT: crosses above 70 | LONG: crosses below 30

    // ── Rule 2 — Pivot touch + rejection ───────
    PivotLevels DailyPivots,
    decimal?    TouchedPivotLevel,
    string      TouchedPivotLabel,
    bool        PivotTouchWithRejection,  // SHORT: bearish rejection at R-level | LONG: bullish bounce at S-level

    // ── Rule 3 — Weekly level tests ────────────
    PivotLevels WeeklyPivots,
    int         WeeklyTestCount,
    bool        WeeklyTestedMultipleTimes,  // >= 2 tests in 3–5 weeks

    // ── Rule 4 — Confirmation candle ───────────
    bool ConfirmationCandleMet,   // SHORT: bearish close | LONG: bullish close

    // ── Final ──────────────────────────────────
    SignalDirection Signal,
    string?         ClaudeNarrative
);
