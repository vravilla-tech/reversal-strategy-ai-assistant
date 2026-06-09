using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// Builds 25 daily candles whose RSI crosses the upper limit (70) on the last candle.
    /// The series trends up strongly, then the final candle is a new up-close.
    /// </summary>
    private static List<Candle> DailiesWithRsiAbove70()
    {
        // Long uptrend produces RSI > 70
        return CandleBuilder.SeriesWithRsiApproaching(targetRsi: 75, totalCandles: 25);
    }

    /// <summary>
    /// Builds 25 daily candles whose RSI crosses the lower limit (30) on the last candle.
    /// </summary>
    private static List<Candle> DailiesWithRsiBelow30()
    {
        return CandleBuilder.SeriesWithRsiApproaching(targetRsi: 25, totalCandles: 25);
    }

    /// <summary>
    /// Returns 8 weekly candles where <paramref name="testCount"/> of the last 5 completed
    /// weeks tested <paramref name="level"/> from the high side (resistance).
    /// </summary>
    private static List<Candle> WeekliesWithResistanceTests(decimal level, int testCount)
    {
        var weeks = new List<Candle>();
        int tested = 0;

        for (int i = 0; i < 7; i++)   // 7 completed + 1 open = 8 total
        {
            bool shouldTest = tested < testCount && i < 5;
            var high = shouldTest ? level + 0.0010m : level - 0.0020m;
            if (shouldTest) tested++;

            weeks.Add(new Candle(
                Timestamp: DateTime.UtcNow.Date.AddDays(-(7 - i) * 7),
                Open:  level - 0.0050m,
                High:  high,
                Low:   level - 0.0100m,
                Close: level - 0.0040m,
                Volume: 1000
            ));
        }

        // Add current (open) weekly candle
        weeks.Add(CandleBuilder.Doji(level - 0.0030m,
            high: level - 0.0015m, low: level - 0.0060m,
            date: DateTime.UtcNow.Date));

        return weeks;
    }

    private static List<Candle> WeekliesWithSupportTests(decimal level, int testCount)
    {
        var weeks = new List<Candle>();
        int tested = 0;

        for (int i = 0; i < 7; i++)
        {
            bool shouldTest = tested < testCount && i < 5;
            var low = shouldTest ? level - 0.0010m : level + 0.0020m;
            if (shouldTest) tested++;

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
        // Build a daily series that ends with RSI breaking above 70
        var daily = DailiesWithRsiAbove70();

        // Compute the pivots that will be used from daily[^2]
        var pivots    = _indicators.ComputePivots(daily[^2]);
        var r1        = pivots.R1;

        // Replace last candle with a bearish-rejection candle at R1
        var lastDate  = daily[^1].Timestamp;
        var openPrice = r1 - 0.0010m;
        daily[^1] = new Candle(
            Timestamp: lastDate,
            Open:  openPrice,
            High:  r1 + 0.0025m,   // wick above R1
            Low:   openPrice - 0.0020m,
            Close: r1 - 0.0020m,   // closes below R1 (bearish rejection)
            Volume: 1000
        );

        var weekly = WeekliesWithResistanceTests(r1, testCount: 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Short, daily, weekly);

        result.Signal.Should().Be(SignalDirection.Short);
        result.RsiBreakingLimit         .Should().BeTrue();
        result.PivotTouchWithRejection  .Should().BeTrue();
        result.WeeklyTestedMultipleTimes.Should().BeTrue();
        result.ConfirmationCandleMet    .Should().BeTrue();
        result.WeeklyTestCount          .Should().BeGreaterOrEqualTo(2);
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
        var daily  = DailiesWithRsiBelow30();
        var pivots = _indicators.ComputePivots(daily[^2]);
        var s1     = pivots.S1;

        var lastDate = daily[^1].Timestamp;
        // Hammer candle: wicks below S1, closes above it (bullish)
        daily[^1] = new Candle(
            Timestamp: lastDate,
            Open:  s1 + 0.0010m,
            High:  s1 + 0.0025m,
            Low:   s1 - 0.0020m,   // wick below S1
            Close: s1 + 0.0018m,   // closes above S1
            Volume: 1000
        );

        var weekly = WeekliesWithSupportTests(s1, testCount: 3);

        var result = await _sut.EvaluateAsync("EUR/USD", SignalDirection.Long, daily, weekly);

        result.Signal.Should().Be(SignalDirection.Long);
        result.RsiBreakingLimit         .Should().BeTrue();
        result.PivotTouchWithRejection  .Should().BeTrue();
        result.WeeklyTestedMultipleTimes.Should().BeTrue();
        result.ConfirmationCandleMet    .Should().BeTrue();
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
