using FluentAssertions;
using ReversalStrategy.Api.Models;
using ReversalStrategy.Api.Services;
using ReversalStrategy.Tests.Helpers;

namespace ReversalStrategy.Tests;

public class IndicatorEngineTests
{
    private readonly IndicatorEngine _sut = new();

    // ══════════════════════════════════════════════════════════════
    // RSI — basic properties
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ComputeRsi_ThrowsWhenNotEnoughCandles()
    {
        var candles = CandleBuilder.FlatSeries(1.10m, 10);
        _sut.Invoking(e => e.ComputeRsi(candles))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeRsi_Returns100_WhenOnlyGainsExist()
    {
        // 20 candles all increasing by 10 pips → no losses → RSI = 100
        var candles = Enumerable.Range(0, 20)
            .Select(i => CandleBuilder.Bullish(1.1000m + i * 0.0010m, 1.1005m + i * 0.0010m,
                date: DateTime.UtcNow.Date.AddDays(i - 20)))
            .ToList();

        var (curr, _) = _sut.ComputeRsi(candles);
        curr.Should().Be(100m);
    }

    [Fact]
    public void ComputeRsi_ReturnsValueBetween0And100()
    {
        var candles = CandleBuilder.SeriesWithRsiApproaching(50, 25);
        var (curr, prev) = _sut.ComputeRsi(candles);
        curr.Should().BeInRange(0, 100);
        prev.Should().BeInRange(0, 100);
    }

    // ══════════════════════════════════════════════════════════════
    // RSI limit-break detectors
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(69.9, 70.1, true)]   // just crosses above
    [InlineData(60.0, 75.0, true)]   // well above
    [InlineData(70.0, 72.0, false)]  // was already at 70 — not a new break
    [InlineData(72.0, 73.0, false)]  // both above — no crossover
    [InlineData(68.0, 69.9, false)]  // never reached 70
    public void IsRsiBreakingUpperLimit_ReturnsExpected(
        double prev, double curr, bool expected)
    {
        _sut.IsRsiBreakingUpperLimit((decimal)curr, (decimal)prev).Should().Be(expected);
    }

    [Theory]
    [InlineData(30.1, 29.9, true)]   // just crosses below
    [InlineData(40.0, 25.0, true)]   // well below
    [InlineData(30.0, 28.0, false)]  // was already at 30 — not a new break
    [InlineData(28.0, 27.0, false)]  // both below — no crossover
    [InlineData(32.0, 30.1, false)]  // never reached 30
    public void IsRsiBreakingLowerLimit_ReturnsExpected(
        double prev, double curr, bool expected)
    {
        _sut.IsRsiBreakingLowerLimit((decimal)curr, (decimal)prev).Should().Be(expected);
    }

    // ══════════════════════════════════════════════════════════════
    // Pivot points
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ComputePivots_ReturnsCorrectPivotPoint()
    {
        // Classic hand-calculated example: H=1.1100 L=1.0900 C=1.1000
        // Pivot = (1.1100 + 1.0900 + 1.1000) / 3 = 1.10000
        var candle = CandleBuilder.Bearish(open: 1.1000m, close: 1.1000m,
            high: 1.1100m, low: 1.0900m);

        var pivots = _sut.ComputePivots(candle);

        pivots.Pivot.Should().Be(1.10000m);
    }

    [Fact]
    public void ComputePivots_R1_IsAbovePivot()
    {
        var candle = CandleBuilder.Bearish(open: 1.1050m, close: 1.0950m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(candle);

        pivots.R1.Should().BeGreaterThan(pivots.Pivot);
        pivots.R2.Should().BeGreaterThan(pivots.R1);
        pivots.R3.Should().BeGreaterThan(pivots.R2);
    }

    [Fact]
    public void ComputePivots_S1_IsBelowPivot()
    {
        var candle = CandleBuilder.Bearish(open: 1.1050m, close: 1.0950m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(candle);

        pivots.S1.Should().BeLessThan(pivots.Pivot);
        pivots.S2.Should().BeLessThan(pivots.S1);
        pivots.S3.Should().BeLessThan(pivots.S2);
    }

    // ══════════════════════════════════════════════════════════════
    // Rule 2 — SHORT: resistance rejection
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CheckResistanceRejection_ReturnsLevel_WhenBearishRejectionAtR1()
    {
        // Pivots where R1 = 1.1060
        var prevCandle = CandleBuilder.Bearish(open: 1.1050m, close: 1.0960m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(prevCandle);

        // Today: wick goes above R1, closes below it (bearish body, meaningful upper wick)
        var r1 = pivots.R1;
        var todayCandle = new Candle(
            Timestamp: DateTime.UtcNow.Date,
            Open:  r1 - 0.0010m,        // opens just below R1
            High:  r1 + 0.0020m,        // wicks above R1
            Low:   r1 - 0.0025m,
            Close: r1 - 0.0018m,        // closes below R1 (bearish)
            Volume: 1000
        );

        var result = _sut.CheckResistanceRejection(todayCandle, pivots);

        result.Should().NotBeNull();
        result!.Value.Label.Should().Be("Daily R1");
    }

    [Fact]
    public void CheckResistanceRejection_ReturnsNull_WhenCandleClosesAboveResistance()
    {
        var prevCandle = CandleBuilder.Bearish(open: 1.1050m, close: 1.0960m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(prevCandle);

        // Bullish breakout — candle closes ABOVE R1 → no rejection
        var r1 = pivots.R1;
        var todayCandle = CandleBuilder.Bullish(
            open:  r1 - 0.0010m,
            close: r1 + 0.0015m,   // closes above R1
            high:  r1 + 0.0020m,
            low:   r1 - 0.0015m
        );

        _sut.CheckResistanceRejection(todayCandle, pivots).Should().BeNull();
    }

    [Fact]
    public void CheckResistanceRejection_ReturnsNull_WhenNoPivotLevelTouched()
    {
        var prevCandle = CandleBuilder.Bearish(open: 1.1050m, close: 1.0960m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(prevCandle);

        // Candle is far below all resistance levels
        var candle = CandleBuilder.Bearish(open: 1.0700m, close: 1.0680m,
            high: 1.0710m, low: 1.0670m);

        _sut.CheckResistanceRejection(candle, pivots).Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════
    // Rule 2 — LONG: support bounce
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CheckSupportBounce_ReturnsLevel_WhenBullishBounceAtS1()
    {
        var prevCandle = CandleBuilder.Bullish(open: 1.0950m, close: 1.1040m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(prevCandle);

        var s1 = pivots.S1;
        // Hammer candle: wicks below S1, closes above it
        var todayCandle = new Candle(
            Timestamp: DateTime.UtcNow.Date,
            Open:  s1 + 0.0010m,
            High:  s1 + 0.0025m,
            Low:   s1 - 0.0020m,        // wicks below S1
            Close: s1 + 0.0018m,        // closes above S1 (bullish)
            Volume: 1000
        );

        var result = _sut.CheckSupportBounce(todayCandle, pivots);

        result.Should().NotBeNull();
        result!.Value.Label.Should().Be("Daily S1");
    }

    [Fact]
    public void CheckSupportBounce_ReturnsNull_WhenCandleClosesBelowSupport()
    {
        var prevCandle = CandleBuilder.Bullish(open: 1.0950m, close: 1.1040m,
            high: 1.1100m, low: 1.0900m);
        var pivots = _sut.ComputePivots(prevCandle);

        var s1 = pivots.S1;
        // Candle breaks through S1 and closes below it — no bounce
        var candle = CandleBuilder.Bearish(
            open:  s1 + 0.0005m,
            close: s1 - 0.0015m,   // closes below S1
            high:  s1 + 0.0010m,
            low:   s1 - 0.0020m
        );

        _sut.CheckSupportBounce(candle, pivots).Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════
    // Rule 3 — Weekly level test count
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void CountWeeklyResistanceTests_CountsCorrectly()
    {
        decimal level = 1.1050m;

        // 5 completed + 1 "current open" weekly candles
        // 3 of the completed weeks tested the level (High >= 1.1050)
        var weeks = new List<Candle>
        {
            CandleBuilder.Bullish(1.09m, 1.10m, high: 1.1060m, low: 1.08m),   // tested ✓
            CandleBuilder.Bearish(1.10m, 1.09m, high: 1.1020m, low: 1.08m),   // not tested
            CandleBuilder.Bullish(1.09m, 1.10m, high: 1.1070m, low: 1.08m),   // tested ✓
            CandleBuilder.Bearish(1.10m, 1.09m, high: 1.1000m, low: 1.08m),   // not tested
            CandleBuilder.Bullish(1.09m, 1.10m, high: 1.1055m, low: 1.08m),   // tested ✓
            CandleBuilder.Doji   (1.10m,         high: 1.1030m, low: 1.09m),   // current week (open) — excluded
        };

        var count = _sut.CountWeeklyResistanceTests(weeks, level, lookbackWeeks: 5);

        count.Should().Be(3);
    }

    [Fact]
    public void CountWeeklySupportTests_CountsCorrectly()
    {
        decimal level = 1.0920m;

        var weeks = new List<Candle>
        {
            CandleBuilder.Bearish(1.10m, 1.09m, high: 1.10m, low: 1.0910m),  // tested ✓ (low <= 1.0920)
            CandleBuilder.Bullish(1.09m, 1.10m, high: 1.11m, low: 1.0930m),  // not tested
            CandleBuilder.Bearish(1.10m, 1.09m, high: 1.10m, low: 1.0915m),  // tested ✓
            CandleBuilder.Bullish(1.09m, 1.10m, high: 1.11m, low: 1.0940m),  // not tested
            CandleBuilder.Bearish(1.10m, 1.09m, high: 1.10m, low: 1.0925m),  // not tested (0925 > 0920)
            CandleBuilder.Doji   (1.09m,         high: 1.10m, low: 1.09m),   // current week — excluded
        };

        var count = _sut.CountWeeklySupportTests(weeks, level, lookbackWeeks: 5);

        count.Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════════
    // Rule 4 — Candle direction
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void IsBearishCandle_ReturnsTrueWhenCloseIsLowerThanOpen()
    {
        var c = CandleBuilder.Bearish(open: 1.1010m, close: 1.0990m);
        _sut.IsBearishCandle(c).Should().BeTrue();
    }

    [Fact]
    public void IsBearishCandle_ReturnsFalseWhenCloseIsHigherThanOpen()
    {
        var c = CandleBuilder.Bullish(open: 1.0990m, close: 1.1010m);
        _sut.IsBearishCandle(c).Should().BeFalse();
    }

    [Fact]
    public void IsBullishCandle_ReturnsTrueWhenCloseIsHigherThanOpen()
    {
        var c = CandleBuilder.Bullish(open: 1.0990m, close: 1.1010m);
        _sut.IsBullishCandle(c).Should().BeTrue();
    }

    [Fact]
    public void IsBullishCandle_ReturnsFalseWhenCloseIsLowerThanOpen()
    {
        var c = CandleBuilder.Bearish(open: 1.1010m, close: 1.0990m);
        _sut.IsBullishCandle(c).Should().BeFalse();
    }

    // ══════════════════════════════════════════════════════════════
    // Wick ratio helpers
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void UpperWickRatio_IsZeroForZeroRangeCandle()
    {
        var c = new Candle(DateTime.UtcNow, 1.10m, 1.10m, 1.10m, 1.10m, 0);
        IndicatorEngine.UpperWickRatio(c).Should().Be(0m);
    }

    [Fact]
    public void UpperWickRatio_IsHighForShootingStar()
    {
        // Open 1.10, Close 1.10, High 1.14, Low 1.09  →  upper wick = 0.04, range = 0.05 → ratio = 0.8
        var c = new Candle(DateTime.UtcNow, 1.10m, 1.14m, 1.09m, 1.10m, 0);
        IndicatorEngine.UpperWickRatio(c).Should().BeGreaterThan(0.5m);
    }
}
