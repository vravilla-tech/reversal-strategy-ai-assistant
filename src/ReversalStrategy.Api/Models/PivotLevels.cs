namespace ReversalStrategy.Api.Models;

public record PivotLevels(
    decimal Pivot,
    decimal R1, decimal R2, decimal R3,
    decimal S1, decimal S2, decimal S3
);
