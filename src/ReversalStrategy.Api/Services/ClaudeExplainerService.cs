using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Sends the computed signal evaluation to Claude and returns a plain-English
/// trading analyst narrative explaining the SHORT setup.
/// </summary>
public class ClaudeExplainerService(IConfiguration config, ILogger<ClaudeExplainerService> logger)
{
    private const string Model = "claude-opus-4-5";

    public async Task<string> NarrateSignalAsync(SignalResult signal)
    {
        var apiKey = config["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is not configured.");

        var client = new AnthropicClient(apiKey);

        var systemPrompt = """
            You are a professional FX trading analyst specialising in SHORT reversal setups.
            Your role is to review computed technical indicator data and write a concise,
            clear analyst commentary (4-6 sentences) walking through each of the 4 strategy rules
            and explaining whether this setup qualifies for a SHORT signal.
            Reference the actual numbers. Be direct and structured.
            Do NOT give financial advice or recommend actual trades. Analyse the technicals only.
            """;

        var allRulesMet = signal.Signal == SignalDirection.Short;

        var userMessage = $"""
            Please analyse this SHORT Reversal Strategy evaluation for {signal.Symbol}.

            === Strategy Rules Evaluated ===

            RULE 1 — RSI(14) Daily Breaks Above 70 (Overbought Breakout)
            Previous RSI : {signal.PreviousRsi}
            Current RSI  : {signal.Rsi}
            Rule 1 Met   : {signal.RsiBreakingUpperLimit}
            (Requires: previous RSI < 70 AND current RSI >= 70)

            RULE 2 — Price Touches Daily Pivot Resistance + Bearish Rejection
            Daily Pivot  : {signal.DailyPivots.Pivot}
            Daily R1     : {signal.DailyPivots.R1}
            Daily R2     : {signal.DailyPivots.R2}
            Daily R3     : {signal.DailyPivots.R3}
            Current Price: {signal.CurrentPrice}
            Touched Level: {signal.TouchedPivotLabel} @ {signal.TouchedPivotLevel?.ToString("F5") ?? "N/A"}
            Rule 2 Met   : {signal.PivotTouchWithRejection}
            (Requires: candle High touched R-level intrabar AND close < level AND bearish candle body)

            RULE 3 — Weekly Candles: Level Tested 2+ Times in Last 5 Weeks
            Weekly R1    : {signal.WeeklyPivots.R1}
            Weekly R2    : {signal.WeeklyPivots.R2}
            Weekly R3    : {signal.WeeklyPivots.R3}
            Tests Found  : {signal.WeeklyTestCount} out of last 5 weeks
            Rule 3 Met   : {signal.WeeklyTestedMultipleTimes}
            (Requires: >= 2 weekly candles with High >= resistance level)

            RULE 4 — Latest Completed Daily Candle is Bearish
            Daily Candle Bearish: {signal.DailyCandleBearish}
            (Requires: close < open on the completed daily candle)

            === Final Signal ===
            Signal: {signal.Signal}
            Evaluated At: {signal.EvaluatedAt:yyyy-MM-dd}

            {(allRulesMet
                ? "All 4 rules are satisfied. Explain why this is a valid SHORT setup."
                : $"Not all rules are satisfied. Explain which rules passed, which failed, and what is missing for a SHORT signal.")}

            Write your analyst commentary now:
            """;

        logger.LogInformation("Sending {Symbol} to Claude for SHORT signal narration", signal.Symbol);

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = userMessage }
        };

        var request = new MessageParameters
        {
            Model     = Model,
            MaxTokens = 450,
            System    = [new SystemMessage(systemPrompt)],
            Messages  = messages
        };

        var response = await client.Messages.GetClaudeMessageAsync(request);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text
               ?? "Claude did not return a narrative.";
    }
}
