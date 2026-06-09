using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ReversalStrategy.Api.Models;
using ReversalStrategy.Api.Services;
using ReversalStrategy.Tests.Helpers;

namespace ReversalStrategy.Tests;

/// <summary>
/// Tests the 4-step rule chain for both SHORT and LONG directions.
/// Each test builds a synthetic daily+weekly candle series that is
/// designed to pass or fail specific rules, then asserts the outcome.
/// </summary>
public class ReversalStrategyEngineTests
{
    private readonly IndicatorEngine       _indicators = new();
    private readonly ReversalStrategyEngine _sut;

    public ReversalStrategyEngineTests()
    {
        _sut = new ReversalStrategyEngine(
            _indicators,
            NullLogger<ReversalStrategyEngine>.Instance);
    }

    // ══════════════════════════════════════════════════════════════
    // Helpers — build candle series for each scenario
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds 25 daily candles whose RSI is above 70 on the last candle
    /// (used for rule-failure tests where RSI just needs to be high, not crossing).
    /// </summary>
    private static List<Candle> DailiesWithRsiAbove70()
        => CandleBuilder.SeriesWithRsiApproaching(targetRsi: 75, totalCandles: 25);

    /// <summary>
    /// Builds 25 daily candles whose RSI is below 30 on the last candle
    /// (used for rule-failure tests where RSI just needs to be low, not crossing).
    /// </summary>
    private static List<Candle> DailiesWithRsiBelow30()
        => CandleBuilder.SeriesWithRsiApproaching(targetRsi: 25, totalCandles: 25);

    /// <summary>
    /// Builds a 30-candle series with a guaranteed RSI crossing above 70 on the last candle.
    ///
    /// Math (Wilder's smoothing, starting avgGain = avgLoss = 0.0005):
    ///   After 28 alternating candles:       avgGain ≈ avgLoss ≈ 0.0005  (RSI ~50)
    ///   After 1 big up (+0.0079):           prevRsi ≈ 68.9              (RSI &lt; 70 ✓)
    ///   Signal candle net gain +0.0010:     currRsi ≈ 70.4              (RSI ≥ 70 ✓)
    ///   → IsRsiBreakingUpperLimit = true (deterministic crossing)
    ///
    /// Signal candle (last): wicks above R1, closes below R1 with bearish body (≥30% upper wick).
    /// </summary>
    private List<Candle> BuildShortSignalCandles(out decimal r1Level)
    {
        var candles = new List<Candle>();
        decimal price = 1.10000m;
        var start = DateTime.UtcNow.Date.AddDays(-30);

        // 28 alternating candles → avgGain ≈ avgLoss ≈ 0.0005, RSI ~50
        for (int i = 0; i < 28; i++)
        {
            var change = i % 2 == 0 ? 0.0010m : -0.0010m;
            var o = price; var c = price + change;
            candles.Add(new Candle(start.AddDays(i), o, Math.Max(o,c)+0.0002m, Math.Min(o,c)-0.0002m, c, 1000));
            price = c;
        }
        // price back to 1.10000m after 14 symmetric pairs

        // 1 trend candle: net gain exactly +0.0079 → prevRsi = 68.9 (just below 70)
        {
            var o = price; var c = price + 0.0079m;
            candles.Add(new Candle(start.AddDays(28), o, c+0.0002m, o-0.0002m, c, 1000));
            price = c;   // 1.10790
        }

        // Pivots are computed by the engine from daily[^2] = candles[^1] at this point
        var pivots = _indicators.ComputePivots(candles[^1]);
        r1Level = pivots.R1;   // ≈ 1.11074

        // Signal candle: net gain +0.0020 from prev close → currRsi ≈ 70.4  (crosses above 70)
        // Also a bearish rejection at R1: open above close, high wicks above R1, close below R1
        var sClose = price + 0.0020m;   // 1.10990  (above prev close but below R1)
        var sOpen  = price + 0.0030m;   // 1.11090  (above close → bearish body)
        var sHigh  = r1Level + 0.0020m; // 1.11274  (wick above R1)
        var sLow   = sClose - 0.0010m;  // 1.10890  (range for upper wick ratio ≈ 48%)

        candles.Add(new Candle(start.AddDays(29), sOpen, sHigh, sLow, sClose, 1000));
        return candles;
    }

    /// <summary>
    /// Mirror of <see cref="BuildShortSignalCandles"/> for a guaranteed RSI crossing below 30.
    ///
    ///   After 28 alternating:               RSI ~50
    ///   After 1 big down (-0.0079):         prevRsi ≈ 31.1              (RSI &gt; 30 ✓)
    ///   Signal candle net loss -0.0010:     currRsi ≈ 29.6              (RSI ≤ 30 ✓)
    ///   → IsRsiBreakingLowerLimit = true
    ///
    /// Signal candle: wicks below S1, closes above S1 with bullish body (≥30% lower wick).
    /// </summary>
    private List<Candle> BuildLongSignalCandles(out decimal s1Level)
    {
        var candles = new List<Candle>();
        decimal price = 1.10000m;
        var start = DateTime.UtcNow.Date.AddDays(-30);

        // 28 alternating candles → RSI ~50
        for (int i = 0; i < 28; i++)
        {
            var change = i % 2 == 0 ? 0.0010m : -0.0010m;
            var o = price; var c = price + change;
            candles.Add(new Candle(start.AddDays(i), o, Math.Max(o,c)+0.0002m, Math.Min(o,c)-0.0002m, c, 1000));
            price = c;
        }

        // 1 trend candle: net loss exactly -0.0070 → prevRsi = 30.8 (just above 30)
        {
            var o = price; var c = price - 0.0070m;
            candles.Add(new Candle(start.AddDays(28), o, o+0.0002m, c-0.0002m, c, 1000));
            price = c;   // 1.09210
        }

        var pivots = _indicators.ComputePivots(candles[^1]);
        s1Level = pivots.S1;   // ≈ 1.08932

        // Signal candle: net loss -0.0010 → currRsi ≈ 29.6 (crosses below 30)
        // Bullish bounce at S1: close above open, low wicks below S1, close above S1
        var sClose = price - 0.0010m;   // 1.09110  (below prev close but above S1)
        var sOpen  = price - 0.0016m;   // 1.09050  (below close → bullish body)
        var sLow   = s1Level - 0.0020m; // 1.08732  (wick below S1)
        var sHigh  = sClose + 0.0010m;  // 1.09210  (range for wick ratio ≈ 67%)

        candles.Add(new Candle(start.AddDays(29), sOpen, sHigh, sLow, sClose, 1000));
        return candles;
    }

    /// <summary>
    /// Returns 8 weekly candles where <paramref name="testCount"/> of the last 5 completed
    /// weeks tested <paramref name="level"/> from the high side (resistance).
    /// </summary>
    private static List<Candle> WeekliesWithResistanceTests(decimal level, int testCount)
    {
        var weeks = new List<Candle>();

        // Place tests in the LAST testCount of the 7 completed candles so they
        // fall inside the engine's 5-week lookback window (last 5 completed).
        for (int i = 0; i < 7; i++)
        {
            bool shouldTest = i >= (7 - testCount);
            var high = shouldTest ? level + 0.0010m : level - 0.0020m;

            weeks.Add(new Candle(
                Timestamp: DateTime.UtcNow.Date.AddDays(-(7 - i) * 7),
                Open:  level - 0.0050m,
                High:  high,
                Low:   level - 0.0100m,
                Close: level - 0.0040m,
                Volume: 1000
            ));
        }

        // Add current (open) weekly candle — excluded by the engine's SkipLast(1)
        weeks.Add(CandleBuilder.Doji(level - 0.0030m,
            high: level - 0.0015m, low: level - 0.0060m,
            date: DateTime.UtcNow.Date));

        return weeks;
    }

    private static List<Candle> WeekliesWithSupportTests(decimal level, int testCount)
    {
        var weeks = new List<Candle>();

        for (int i = 0; i < 7; i++)
        {
            bool shouldTest = i >= (7 - testCount);
            var low = shouldTest ? level - 0.0010m : level + 0.0020m;

            weeks.Add(new Candle(
                Timestamp: DateTime.UtcNow.Date.AddDays(-(7 - i) * 7),
                Open:  level + 0.0050m,
                High:  level + 0.0100m,
                Low:   low,
                Close: level + 0.0040m,
                Volume: 1000
            ));
        }

        weeks.Add(CandleBuilder.Doji(level + 0.0030m,
            high: level + 0.0060m, low: level + 0.0015m,
            date: DateTime.UtcNow.Date));

        return weeks;
    }

    // ══════════════════════════════════════════════════════════════
    // SHORT — full signal (all 4 rules met)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Short_AllRulesMet_ProducesShortSignal()
    {
        // Deterministically crafted series: prev RSI ~69.2, curr RSI ~72 (guaranteed crossing)
        var daily  = BuildShortSignalCandles(out var r1);
        var weekly = WeekliesWithResistanceTests(r1, testCount: 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.RsiBreakingLimit         .Should().BeTrue("prev RSI ~69, curr RSI ~72 — crossing confirmed");
        result.PivotTouchWithRejection  .Should().BeTrue("signal candle wicks above R1 and closes below it");
        result.WeeklyTestedMultipleTimes.Should().BeTrue("3 weekly tests provided");
        result.ConfirmationCandleMet    .Should().BeTrue("signal candle close < open");
        result.WeeklyTestCount          .Should().BeGreaterOrEqualTo(2);
        result.Signal                   .Should().Be(SignalDirection.Short);
    }

    // ══════════════════════════════════════════════════════════════
    // SHORT — rule-by-rule failure scenarios
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Short_Rule1Fails_WhenRsiNotAbove70()
    {
        // Downtrend series → RSI will be low, not above 70
        var daily  = DailiesWithRsiBelow30();
        var weekly = WeekliesWithResistanceTests(1.1050m, 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.RsiBreakingLimit.Should().BeFalse();
        result.Signal          .Should().Be(SignalDirection.None);
    }

    [Fact]
    public async Task Short_Rule2Fails_WhenNoPivotRejection()
    {
        var daily  = DailiesWithRsiAbove70();

        // Replace last candle with a plain bullish candle far from any pivot level
        var lastDate = daily[^1].Timestamp;
        daily[^1] = CandleBuilder.Bullish(open: 0.9800m, close: 0.9820m,
            high: 0.9825m, low: 0.9790m, date: lastDate);

        var weekly = WeekliesWithResistanceTests(1.1050m, 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.PivotTouchWithRejection.Should().BeFalse();
        result.Signal                 .Should().Be(SignalDirection.None);
    }

    [Fact]
    public async Task Short_Rule3Fails_WhenWeeklyTestsAreLessThan2()
    {
        var daily  = DailiesWithRsiAbove70();
        var pivots = _indicators.ComputePivots(daily[^2]);
        var r1     = pivots.R1;

        var lastDate = daily[^1].Timestamp;
        daily[^1] = new Candle(lastDate, r1 - 0.0010m, r1 + 0.0025m,
            r1 - 0.0020m, r1 - 0.0020m, 1000);

        // Only 1 weekly test — should fail Rule 3
        var weekly = WeekliesWithResistanceTests(r1, testCount: 1);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.WeeklyTestedMultipleTimes.Should().BeFalse();
        result.Signal                   .Should().Be(SignalDirection.None);
    }

    [Fact]
    public async Task Short_Rule4Fails_WhenLastCandleIsBullish()
    {
        var daily  = DailiesWithRsiAbove70();
        var pivots = _indicators.ComputePivots(daily[^2]);
        var r1     = pivots.R1;

        var lastDate = daily[^1].Timestamp;

        // Candle wicks above R1 but CLOSES ABOVE it (bullish) → Rule 2 also fails (closed above)
        // So use a candle that closes below R1 (Rule 2 passes) but is bullish body
        // Actually if close > open AND close < r1 → unusual (inverted hammer?), let's just use
        // a candle where close < r1 but close > open (rare but valid for testing Rule 4 isolation)
        daily[^1] = new Candle(
            Timestamp: lastDate,
            Open:  r1 - 0.0018m,   // open below R1
            High:  r1 + 0.0025m,   // wick above
            Low:   r1 - 0.0025m,
            Close: r1 - 0.0005m,   // close below R1 but above open → bullish body
            Volume: 1000
        );

        var weekly = WeekliesWithResistanceTests(r1, testCount: 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.ConfirmationCandleMet.Should().BeFalse();
        result.Signal               .Should().Be(SignalDirection.None);
    }

    // ══════════════════════════════════════════════════════════════
    // LONG — full signal (all 4 rules met)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Long_AllRulesMet_ProducesLongSignal()
    {
        // Deterministically crafted series: prev RSI ~30.8, curr RSI ~28 (guaranteed crossing)
        var daily  = BuildLongSignalCandles(out var s1);
        var weekly = WeekliesWithSupportTests(s1, testCount: 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Long, daily, weekly);

        result.RsiBreakingLimit         .Should().BeTrue("prev RSI ~30.8, curr RSI ~28 — crossing confirmed");
        result.PivotTouchWithRejection  .Should().BeTrue("signal candle wicks below S1 and closes above it");
        result.WeeklyTestedMultipleTimes.Should().BeTrue("3 weekly tests provided");
        result.ConfirmationCandleMet    .Should().BeTrue("signal candle close > open");
        result.Signal                   .Should().Be(SignalDirection.Long);
    }

    // ══════════════════════════════════════════════════════════════
    // LONG — rule-by-rule failure scenarios
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Long_Rule1Fails_WhenRsiNotBelow30()
    {
        var daily  = DailiesWithRsiAbove70();   // RSI is high — Rule 1 for LONG fails
        var weekly = WeekliesWithSupportTests(1.0850m, 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Long, daily, weekly);

        result.RsiBreakingLimit.Should().BeFalse();
        result.Signal          .Should().Be(SignalDirection.None);
    }

    [Fact]
    public async Task Long_Rule3Fails_WhenWeeklyTestsAreLessThan2()
    {
        var daily  = DailiesWithRsiBelow30();
        var pivots = _indicators.ComputePivots(daily[^2]);
        var s1     = pivots.S1;

        var lastDate = daily[^1].Timestamp;
        daily[^1] = new Candle(lastDate, s1 + 0.0010m, s1 + 0.0025m,
            s1 - 0.0020m, s1 + 0.0018m, 1000);

        // Only 1 weekly test
        var weekly = WeekliesWithSupportTests(s1, testCount: 1);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Long, daily, weekly);

        result.WeeklyTestedMultipleTimes.Should().BeFalse();
        result.Signal                   .Should().Be(SignalDirection.None);
    }

    // ══════════════════════════════════════════════════════════════
    // EvaluateBothAsync — returns two independent results
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task EvaluateBothAsync_ReturnsTwoResults_WithCorrectDirections()
    {
        var daily  = DailiesWithRsiAbove70();
        var weekly = WeekliesWithResistanceTests(1.1050m, 3);

        var (shortResult, longResult) = await _sut.EvaluateBothAsync("GBP/USD", daily, weekly);

        shortResult.Direction.Should().Be(SignalDirection.Short);
        longResult .Direction.Should().Be(SignalDirection.Long);
        shortResult.Symbol.Should().Be("GBP/USD");
        longResult .Symbol.Should().Be("GBP/USD");
    }

    [Fact]
    public async Task EvaluateBothAsync_ThrowsOnInsufficientDailyCandles()
    {
        var tinyDaily  = CandleBuilder.FlatSeries(1.10m, 5);
        var weekly     = WeekliesWithResistanceTests(1.10m, 2);

        await _sut.Invoking(e => e.EvaluateBothAsync("EUR/USD", tinyDaily, weekly))
            .Should().ThrowAsync<ArgumentException>();
    }
}
