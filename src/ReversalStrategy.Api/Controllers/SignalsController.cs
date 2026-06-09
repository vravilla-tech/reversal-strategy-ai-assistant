using Microsoft.AspNetCore.Mvc;
using ReversalStrategy.Api.Models;
using ReversalStrategy.Api.Services;

namespace ReversalStrategy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SignalsController(
    MarketDataService       marketData,
    ReversalStrategyEngine  strategyEngine,
    ClaudeExplainerService  claudeExplainer,
    ILogger<SignalsController> logger) : ControllerBase
{
    private static readonly string[] DefaultPairs =
        ["EUR/USD", "GBP/USD", "USD/JPY", "AUD/USD", "USD/CAD", "USD/CHF", "NZD/USD"];

    /// <summary>
    /// Evaluates the SHORT reversal strategy for a single FX pair.
    /// Symbol format: EUR-USD  (use dash in URL, slash used internally)
    /// </summary>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(SignalResult), 200)]
    public async Task<IActionResult> GetSignal(string symbol)
    {
        try
        {
            var formatted = symbol.ToUpper().Replace("-", "/");
            logger.LogInformation("Evaluating SHORT reversal for {Symbol}", formatted);

            var daily  = await marketData.GetDailyCandlesAsync(formatted,  30);
            var weekly = await marketData.GetWeeklyCandlesAsync(formatted,  8);

            var signal = await strategyEngine.EvaluateAsync(formatted, daily, weekly);

            // Always ask Claude — even if no signal, the rule-by-rule explanation is valuable
            var narrative = await claudeExplainer.NarrateSignalAsync(signal);
            signal = signal with { ClaudeNarrative = narrative };

            return Ok(signal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Scans all default FX pairs for SHORT reversal candidates.
    /// Claude is called for any pair where at least Rule 1 (RSI breakout) is met.
    /// </summary>
    [HttpGet("scan")]
    [ProducesResponseType(typeof(List<SignalResult>), 200)]
    public async Task<IActionResult> ScanAllPairs()
    {
        var results = new List<SignalResult>();

        foreach (var pair in DefaultPairs)
        {
            try
            {
                var daily  = await marketData.GetDailyCandlesAsync(pair, 30);
                var weekly = await marketData.GetWeeklyCandlesAsync(pair,  8);

                var signal = await strategyEngine.EvaluateAsync(pair, daily, weekly);

                if (signal.RsiBreakingUpperLimit)
                {
                    var narrative = await claudeExplainer.NarrateSignalAsync(signal);
                    signal = signal with { ClaudeNarrative = narrative };
                }
                else
                {
                    signal = signal with
                    {
                        ClaudeNarrative = $"RSI ({signal.Rsi}) has not broken above 70 on the daily chart — " +
                                          "Rule 1 not met, no further evaluation required."
                    };
                }

                results.Add(signal);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping {Pair}: {Message}", pair, ex.Message);
            }
        }

        // SHORT signals first, then by how many rules passed
        return Ok(results
            .OrderByDescending(r => r.Signal == SignalDirection.Short)
            .ThenByDescending(r => RuleScore(r))
            .ThenBy(r => r.Symbol));
    }

    private static int RuleScore(SignalResult r) =>
        (r.RsiBreakingUpperLimit     ? 1 : 0) +
        (r.PivotTouchWithRejection   ? 1 : 0) +
        (r.WeeklyTestedMultipleTimes ? 1 : 0) +
        (r.DailyCandleBearish        ? 1 : 0);
}
