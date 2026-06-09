using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Tests.Helpers;

/// <summary>
/// Fluent builder for creating test candles without noise.
/// </summary>
public static class CandleBuilder
{
    /// <summary>Creates a simple bullish candle (close > open).</summary>
    public static Candle Bullish(decimal open, decimal close, decimal? high = null, decimal? low = null,
        DateTime? date = null)
        => new(
            Timestamp: date ?? DateTime.UtcNow.Date,
            Open:  open,
            High:  high  ?? close + 0.0010m,
            Low:   low   ?? open  - 0.0010m,
            Close: close,
            Volume: 1000
        );

    /// <summary>Creates a simple bearish candle (close &lt; open).</summary>
    public static Candle Bearish(decimal open, decimal close, decimal? high = null, decimal? low = null,
        DateTime? date = null)
        => new(
            Timestamp: date ?? DateTime.UtcNow.Date,
            Open:  open,
            High:  high  ?? open  + 0.0010m,
            Low:   low   ?? close - 0.0010m,
            Close: close,
            Volume: 1000
        );

    /// <summary>Creates a doji candle (open == close).</summary>
    public static Candle Doji(decimal price, decimal? high = null, decimal? low = null, DateTime? date = null)
        => new(
            Timestamp: date ?? DateTime.UtcNow.Date,
            Open:  price,
            High:  high ?? price + 0.0005m,
            Low:   low  ?? price - 0.0005m,
            Close: price,
            Volume: 1000
        );

    /// <summary>
    /// Creates a series of <paramref name="count"/> flat candles around a base price.
    /// Useful for padding an RSI series.
    /// </summary>
    public static List<Candle> FlatSeries(decimal basePrice, int count, decimal drift = 0m)
    {
        var list = new List<Candle>();
        var price = basePrice;
        var start = DateTime.UtcNow.Date.AddDays(-count);
        for (int i = 0; i < count; i++)
        {
            list.Add(Doji(price, date: start.AddDays(i)));
            price += drift;
        }
        return list;
    }

    /// <summary>
    /// Builds a daily series that will produce an RSI around <paramref name="targetRsi"/>
    /// when computed over the last candle.
    /// Strategy: long declining run (low RSI) or ascending run (high RSI) then snap.
    /// </summary>
    public static List<Candle> SeriesWithRsiApproaching(decimal targetRsi, int totalCandles = 25)
    {
        // Simple heuristic: alternate up/down to produce a mid-range RSI, then force direction.
        var candles = new List<Candle>();
        decimal price = 1.10000m;
        var startDate = DateTime.UtcNow.Date.AddDays(-totalCandles);

        bool wantHigh = targetRsi >= 70;

        for (int i = 0; i < totalCandles; i++)
        {
            decimal change = wantHigh ? 0.00050m : -0.00050m;
            // Alternate slightly to keep RSI realistic
            if (i % 8 == 7) change = -change;

            var o = price;
            var c = price + change;
            price = c;

            candles.Add(new Candle(
                Timestamp: startDate.AddDays(i),
                Open:  o,
                High:  Math.Max(o, c) + 0.0002m,
                Low:   Math.Min(o, c) - 0.0002m,
                Close: c,
                Volume: 1000
            ));
        }
        return candles;
    }
}
