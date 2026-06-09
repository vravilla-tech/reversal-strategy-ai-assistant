namespace ReversalStrategy.Api.Models;

public record Candle(
    DateTime Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume
);
