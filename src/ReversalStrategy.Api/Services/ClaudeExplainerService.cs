using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Sends the computed signal evaluation to Claude and returns a plain-English
/// analyst narrative. Adapts the prompt for both LONG and SHORT setups.
/// </summary>
public class ClaudeExplainerService(IConfiguration config, ILogger<ClaudeExplainerService> logger)
{
    private const string Model = "claude-opus-4-5";

    public async Task<string> NarrateSignalAsync(SignalResult signal)
    {
        var apiKey = config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        var client = new AnthropicClient(apiKey);

        bool isShort   = signal.Direction == SignalDirection.Short;
        var  direction = isShort ? "SHORT" : "LONG";
        var  rsiNote   = isShort ? "crosses above 70 (overbought)" : "crosses below 30 (oversold)";
        var  pivotNote = isShort ? "bearish rejection at resistance (R1/R2/R3)" : "bullish bounce at support (S1/S2/S3)";
        var  candleNote= isShort ? "bearish (close < open)" : "bullish (close > open)";

        var systemPrompt = $"""
            You are a professional FX trading analyst specialising in {direction} reversal setups.
            Review the computed technical data and write a concise analyst commentary (4–6 sentences)
            walking through each of the 4 strategy rules in order, stating clearly whether each passed or failed,
            and concluding whether this is a valid {direction} setup.
            Reference actual numbers. Do NOT give financial advice or recommend actual trades.
            """;

        var userMessage = $"""
            Analyse this {direction} Reversal Strategy evaluation for {signal.Symbol}:

            === RULE 1 — RSI(14) Daily {(isShort ? "breaks ABOVE 70" : "breaks BELOW 30")} ===
            Previous RSI : {signal.PreviousRsi}
            Current RSI  : {signal.Rsi}
            Pass         : {signal.RsiBreakingLimit}
            Condition    : RSI {rsiNote}

            === RULE 2 — Price touches pivot {(isShort ? "resistance" : "support")} + {(isShort ? "bearish rejection" : "bullish bounce")} ===
            Daily Pivot  : {signal.DailyPivots.Pivot}
            {(isShort
                ? $"Daily R1: {signal.DailyPivots.R1}  R2: {signal.DailyPivots.R2}  R3: {signal.DailyPivots.R3}"
                : $"Daily S1: {signal.DailyPivots.S1}  S2: {signal.DailyPivots.S2}  S3: {signal.DailyPivots.S3}")}
            Current Price: {signal.CurrentPrice}
            Touched Level: {signal.TouchedPivotLabel} @ {signal.TouchedPivotLevel?.ToString("F5") ?? "N/A"}
            Pass         : {signal.PivotTouchWithRejection}
            Condition    : {pivotNote}

            === RULE 3 — Weekly candles: level tested >= 2 times in last 5 weeks ===
            {(isShort
                ? $"Weekly R1: {signal.WeeklyPivots.R1}  R2: {signal.WeeklyPivots.R2}  R3: {signal.WeeklyPivots.R3}"
                : $"Weekly S1: {signal.WeeklyPivots.S1}  S2: {signal.WeeklyPivots.S2}  S3: {signal.WeeklyPivots.S3}")}
            Tests Found  : {signal.WeeklyTestCount} / 5 weeks
            Pass         : {signal.WeeklyTestedMultipleTimes}

            === RULE 4 — Latest completed daily candle is {candleNote} ===
            Pass         : {signal.ConfirmationCandleMet}

            === RESULT ===
            Signal       : {signal.Signal}
            Date         : {signal.EvaluatedAt:yyyy-MM-dd}

            {(signal.Signal != SignalDirection.None
                ? $"All 4 rules passed. Explain why this is a valid {direction} reversal setup."
                : $"Not all rules passed. Explain which rules passed, which failed, and what is missing for a {direction} signal.")}
            """;

        logger.LogInformation("Sending {Symbol} {Dir} signal to Claude", signal.Symbol, signal.Direction);

        var response = await client.Messages.GetClaudeMessageAsync(new MessageParameters
        {
            Model     = Model,
            MaxTokens = 450,
            System    = [new SystemMessage(systemPrompt)],
            Messages  = [new Message { Role = RoleType.User, Content = userMessage }]
        });

        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text
               ?? "Claude did not return a narrative.";
    }
}
