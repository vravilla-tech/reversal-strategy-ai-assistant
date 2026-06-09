using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

/// <summary>
/// Sends computed signal data to Claude and gets a plain-English trading analyst narrative.
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
            You are a professional FX trading analyst specialising in reversal strategies.
            Your job is to review computed technical indicator data and write a concise,
            clear analyst commentary (3-5 sentences) explaining why this setup does or does
            not qualify as a reversal signal. Be specific — reference the actual numbers.
            Do not give financial advice or recommendations to buy/sell. Explain the technicals only.
            """;

        var userMessage = $"""
            Please analyse the following FX reversal strategy evaluation for {signal.Symbol}:

            === Market Data ===
            Symbol:        {signal.Symbol}
            Evaluated At:  {signal.EvaluatedAt:yyyy-MM-dd}
            Current Price: {signal.CurrentPrice}

            === RSI (14-period) ===
            RSI Value:     {signal.Rsi}
            Condition Met: {signal.RsiConditionMet} (threshold: below 30 = oversold/BUY, above 70 = overbought/SELL)

            === Daily Pivot Points (from previous daily candle) ===
            Pivot:  {signal.DailyPivots.Pivot}
            R1: {signal.DailyPivots.R1}  R2: {signal.DailyPivots.R2}  R3: {signal.DailyPivots.R3}
            S1: {signal.DailyPivots.S1}  S2: {signal.DailyPivots.S2}  S3: {signal.DailyPivots.S3}
            Pivot Alignment Met: {signal.PivotAlignmentMet}

            === Weekly Support/Resistance Levels (from last completed week) ===
            R1: {signal.WeeklyPivots.R1}  R2: {signal.WeeklyPivots.R2}  R3: {signal.WeeklyPivots.R3}
            S1: {signal.WeeklyPivots.S1}  S2: {signal.WeeklyPivots.S2}  S3: {signal.WeeklyPivots.S3}
            Intrabar Touch of Weekly S2/S3 (or R2/R3): {signal.WeeklySrTouchedIntrabar}
            Touched Level: {signal.TouchedLevelLabel} @ {signal.TouchedWeeklyLevel?.ToString() ?? "N/A"}

            === Final Signal ===
            Signal: {signal.Signal}

            Write your analyst commentary now:
            """;

        logger.LogInformation("Sending {Symbol} signal to Claude for narration", signal.Symbol);

        var messages = new List<Message>
        {
            new() { Role = RoleType.User, Content = userMessage }
        };

        var request = new MessageParameters
        {
            Model    = Model,
            MaxTokens = 400,
            System   = [new SystemMessage(systemPrompt)],
            Messages = messages
        };

        var response = await client.Messages.GetClaudeMessageAsync(request);
        return response.Content.OfType<TextContent>().FirstOrDefault()?.Text
               ?? "Claude did not return a narrative.";
    }
}
