namespace ReversalStrategy.Api.Models;

public enum SignalDirection { None, Buy, Sell }

public record SignalResult(
    string Symbol,
    DateTime EvaluatedAt,
    decimal CurrentPrice,
    decimal Rsi,
    PivotLevels DailyPivots,
    PivotLevels WeeklyPivots,
    bool RsiConditionMet,
    bool PivotAlignmentMet,
    bool WeeklySrTouchedIntrabar,
    decimal? TouchedWeeklyLevel,
    string TouchedLevelLabel,
    SignalDirection Signal,
    string? ClaudeNarrative
);
