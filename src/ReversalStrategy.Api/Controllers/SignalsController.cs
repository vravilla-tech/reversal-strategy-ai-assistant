using Microsoft.AspNetCore.Mvc;
using ReversalStrategy.Api.Models;
using ReversalStrategy.Api.Services;

namespace ReversalStrategy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SignalsController(
    MarketDataService      marketData,
    ReversalStrategyEngine strategyEngine,
    ClaudeExplainerService claudeExplainer,
    ILogger<SignalsController> logger) : ControllerBase
{
    private static readonly string[] DefaultPairs =
        ["EUR/USD", "GBP/USD", "USD/JPY", "AUD/USD", "USD/CAD", "USD/CHF", "NZD/USD"];

    /// <summary>
    /// Evaluates BOTH Long and Short reversal setups for a single FX pair.
    /// Symbol format: EUR-USD (dash in URL).
    /// </summary>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(IEnumerable<SignalResult>), 200)]
    public async Task<IActionResult> GetSignal(string symbol)
    {
        try
        {
            var formatted = symbol.ToUpper().Replace("-", "/");
            logger.LogInformation("Evaluating both directions for {Symbol}", formatted);

            var daily  = await marketData.GetDailyCandlesAsync(formatted, 30);
            var weekly = await marketData.GetWeeklyCandlesAsync(formatted,  8);

            var (shortResult, longResult) = await strategyEngine.EvaluateBothAsync(formatted, daily, weekly);

            shortResult = shortResult with { ClaudeNarrative = await claudeExplainer.NarrateSignalAsync(shortResult) };
            longResult  = longResult  with { ClaudeNarrative = await claudeExplainer.NarrateSignalAsync(longResult)  };

            return Ok(new[] { shortResult, longResult });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Scans all default FX pairs for both Long and Short reversal candidates.
    /// Claude is called only when at least Rule 1 (RSI breakout) is met for a direction.
    /// </summary>
    [HttpGet("scan")]
    [ProducesResponseType(typeof(IEnumerable<SignalResult>), 200)]
    public async Task<IActionResult> ScanAllPairs()
    {
        var results = new List<SignalResult>();

        foreach (var pair in DefaultPairs)
        {
            try
            {
                var daily  = await marketData.GetDailyCandlesAsync(pair, 30);
                var weekly = await marketData.GetWeeklyCandlesAsync(pair,  8);

                var (shortResult, longResult) = await strategyEngine.EvaluateBothAsync(pair, daily, weekly);

                results.Add(await WithNarrativeIfRelevant(shortResult));
                results.Add(await WithNarrativeIfRelevant(longResult));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping {Pair}: {Msg}", pair, ex.Message);
            }
        }

        return Ok(results
            .OrderByDescending(r => r.Signal != SignalDirection.None)
            .ThenByDescending(r => RuleScore(r))
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Direction));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<SignalResult> WithNarrativeIfRelevant(SignalResult result)
    {
        if (!result.RsiBreakingLimit)
        {
            var threshold = result.Direction == SignalDirection.Short ? "above 70" : "below 30";
            return result with
            {
                ClaudeNarrative = $"RSI ({result.Rsi}) has not broken {threshold} — " +
                                  $"Rule 1 not met for {result.Direction} setup."
            };
        }

        var narrative = await claudeExplainer.NarrateSignalAsync(result);
        return result with { ClaudeNarrative = narrative };
    }

    private static int RuleScore(SignalResult r) =>
        (r.RsiBreakingLimit          ? 1 : 0) +
        (r.PivotTouchWithRejection   ? 1 : 0) +
        (r.WeeklyTestedMultipleTimes ? 1 : 0) +
        (r.ConfirmationCandleMet     ? 1 : 0);
}
