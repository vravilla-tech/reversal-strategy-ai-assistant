using System.Text.Json;
using ReversalStrategy.Api.Models;

namespace ReversalStrategy.Api.Services;

public class MarketDataService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<MarketDataService> logger)
{
    private readonly string _apiKey = config["TwelveData:ApiKey"] ?? throw new InvalidOperationException("TwelveData:ApiKey is not configured.");

    /// <summary>Fetches the last <paramref name="count"/> daily candles for a symbol.</summary>
    public async Task<List<Candle>> GetDailyCandlesAsync(string symbol, int count = 30)
        => await FetchCandlesAsync(symbol, "1day", count);

    /// <summary>Fetches the last <paramref name="count"/> weekly candles for a symbol.</summary>
    public async Task<List<Candle>> GetWeeklyCandlesAsync(string symbol, int count = 10)
        => await FetchCandlesAsync(symbol, "1week", count);

    private async Task<List<Candle>> FetchCandlesAsync(string symbol, string interval, int count)
    {
        var client = httpClientFactory.CreateClient("TwelveData");
        var url = $"time_series?symbol={symbol}&interval={interval}&outputsize={count}&apikey={_apiKey}";

        logger.LogInformation("Fetching {Interval} candles for {Symbol}", interval, symbol);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var code))
            throw new Exception($"TwelveData API error {code}: {root.GetProperty("message").GetString()}");

        var candles = new List<Candle>();
        foreach (var item in root.GetProperty("values").EnumerateArray())
        {
            candles.Add(new Candle(
                Timestamp: DateTime.Parse(item.GetProperty("datetime").GetString()!),
                Open:  decimal.Parse(item.GetProperty("open").GetString()!),
                High:  decimal.Parse(item.GetProperty("high").GetString()!),
                Low:   decimal.Parse(item.GetProperty("low").GetString()!),
                Close: decimal.Parse(item.GetProperty("close").GetString()!),
                Volume: item.TryGetProperty("volume", out var vol) ? long.Parse(vol.GetString()!) : 0
            ));
        }

        // Return oldest-first
        candles.Reverse();
        return candles;
    }
}
