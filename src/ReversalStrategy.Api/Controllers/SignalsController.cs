using Microsoft.AspNetCore.Mvc;
using ReversalStrategy.Api.Models;
using ReversalStrategy.Api.Services;

namespace ReversalStrategy.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SignalsController(
    MarketDataService marketData,
    ReversalStrategyEngine strategyEngine,
    ClaudeExplainerService claudeExplainer,
    ILogger<SignalsController> logger) : ControllerBase
{
    // Default FX pairs to scan
    private static readonly string[] DefaultPairs =
        ["EUR/USD", "GBP/USD", "USD/JPY", "AUD/USD", "USD/CAD", "USD/CHF", "NZD/USD"];

    /// <summary>
    /// Evaluates the reversal strategy for a single FX pair and returns
    /// indicator data plus Claude's narrative explanation.
    /// </summary>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(SignalResult), 200)]
    public async Task<IActionResult> GetSignal(string symbol)
    {
        try
        {
            var formattedSymbol = symbol.ToUpper().Replace("-", "/");

            logger.LogInformation("Evaluating reversal signal for {Symbol}", formattedSymbol);

            var dailyCandles  = await marketData.GetDailyCandlesAsync(formattedSymbol, 30);
            var weeklyCandles = await marketData.GetWeeklyCandlesAsync(formattedSymbol, 10);

            var signal = await strategyEngine.EvaluateAsync(formattedSymbol, dailyCandles, weeklyCandles);

            var narrative = await claudeExplainer.NarrateSignalAsync(signal);
            signal = signal with { ClaudeNarrative = narrative };

            return Ok(signal);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error evaluating signal for {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Scans all default FX pairs and returns signals with Claude narration.
    /// Only returns pairs where at least the RSI condition is met.
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
                var dailyCandles  = await marketData.GetDailyCandlesAsync(pair, 30);
                var weeklyCandles = await marketData.GetWeeklyCandlesAsync(pair, 10);
                var signal        = await strategyEngine.EvaluateAsync(pair, dailyCandles, weeklyCandles);

                // Only call Claude for candidates where RSI condition is met (save API calls)
                if (signal.RsiConditionMet)
                {
                    var narrative = await claudeExplainer.NarrateSignalAsync(signal);
                    signal = signal with { ClaudeNarrative = narrative };
                }
                else
                {
                    signal = signal with { ClaudeNarrative = "RSI condition not met — no reversal candidate at this time." };
                }

                results.Add(signal);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping {Pair} due to error", pair);
            }
        }

        return Ok(results.OrderByDescending(r => r.Signal).ThenBy(r => r.Symbol));
    }
}
