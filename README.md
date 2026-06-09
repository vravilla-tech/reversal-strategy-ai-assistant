# 📉 FX SHORT Reversal Strategy — AI Assistant

An FX trading signal application that detects **SHORT reversal setups** using a 4-step rules-based strategy engine, combined with an AI narrative layer powered by [Claude](https://claude.ai) by Anthropic.

> Built by a senior .NET developer exploring AI engineering — demonstrating how deterministic quant logic and large language models work together in a production-style .NET 8 application.

---

## 🧠 Strategy Logic

The app evaluates **both LONG and SHORT reversal setups** simultaneously for every FX pair, using the same 4-step rule chain applied in opposite directions:

| Step | Timeframe | SHORT Rule ▼ | LONG Rule ▲ |
|------|-----------|-------------|------------|
| **1** | Daily | RSI(14) **crosses above 70** (overbought) | RSI(14) **crosses below 30** (oversold) |
| **2** | Daily | Candle wicks into **R1/R2/R3**, closes below (bearish rejection) | Candle wicks into **S1/S2/S3**, closes above (bullish bounce) |
| **3** | Weekly | That **resistance** tested ≥ 2× in last 5 weeks | That **support** tested ≥ 2× in last 5 weeks |
| **4** | Daily | Last completed daily candle is **bearish** (close < open) | Last completed daily candle is **bullish** (close > open) |
| ✅ | — | **Signal: SHORT ▼** | **Signal: LONG ▲** |

Claude then receives all computed indicator data and writes a **plain-English analyst commentary** — walking through each rule and explaining the setup like a trading analyst.

---

## 🏗️ Architecture

```
reversal-strategy-ai-assistant/
├── src/
│   └── ReversalStrategy.Api/                    # ASP.NET Core 8 Web API
│       ├── Controllers/
│       │   └── SignalsController.cs             # GET /api/signals/{pair}  &  /api/signals/scan
│       ├── Services/
│       │   ├── MarketDataService.cs             # Fetches OHLC candles from Twelve Data API
│       │   ├── IndicatorEngine.cs               # RSI (Wilder), pivots, rejection/bounce checks, weekly test counter
│       │   ├── ReversalStrategyEngine.cs        # 4-step rule chain for both LONG and SHORT directions
│       │   └── ClaudeExplainerService.cs        # Structured prompt → Claude → analyst narrative (adapts per direction)
│       ├── Models/
│       │   ├── Candle.cs                        # OHLCV record
│       │   ├── PivotLevels.cs                   # P, R1-R3, S1-S3
│       │   └── SignalResult.cs                  # Unified result for both directions (all rule flags + narrative)
│       └── wwwroot/index.html                   # Dark-theme SPA with LONG/SHORT tab switching
├── tests/
│   └── ReversalStrategy.Tests/                  # xUnit test project
│       ├── Helpers/CandleBuilder.cs             # Fluent test-candle builder
│       ├── IndicatorEngineTests.cs              # RSI, pivots, wick ratios, rule checks
│       └── ReversalStrategyEngineTests.cs       # Full 4-rule chain for both directions
└── ReversalStrategy.sln
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Twelve Data API key](https://twelvedata.com) — free tier (800 calls/day)
- [Anthropic API key](https://console.anthropic.com) — Claude API

### 1. Clone

```bash
git clone https://github.com/vravilla-tech/reversal-strategy-ai-assistant.git
cd reversal-strategy-ai-assistant
```

### 2. Add API keys

Edit `src/ReversalStrategy.Api/appsettings.json`:

```json
{
  "TwelveData":  { "ApiKey": "your_twelvedata_key" },
  "Anthropic":   { "ApiKey": "your_anthropic_key"  }
}
```

> ⚠️ Never commit real API keys. Use `appsettings.Development.json` (git-ignored) for local dev.

### 3. Run

```bash
cd src/ReversalStrategy.Api
dotnet run
```

- **Dashboard:** http://localhost:5000
- **Swagger UI:** http://localhost:5000/swagger

---

## 📡 API Reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/signals/{symbol}` | Analyse a single pair — e.g. `/api/signals/EUR-USD` |
| `GET` | `/api/signals/scan` | Scan all 7 major FX pairs for SHORT candidates |
| `GET` | `/health` | Health check |

---

## 🤖 AI Engineering Highlights

- **Structured prompting** — Claude receives a precisely templated data payload per rule, not free text, ensuring grounded and consistent responses
- **LLM as explainability layer** — the deterministic strategy engine decides the signal; Claude's role is interpretation and narration only
- **Sequential rule evaluation** — each rule gates the next, avoiding unnecessary computation and API calls
- **Cost-aware Claude usage** — Claude is only called when at least Rule 1 (RSI breakout) is satisfied; otherwise a lightweight message is returned
- **Bi-directional strategy** — the same 4-step rule chain is applied independently for both LONG and SHORT on every scan, with direction-aware prompts to Claude
- **Clean separation of concerns** — strategy logic is fully testable in isolation without any LLM dependency

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core 8 Web API |
| AI / LLM | Claude (via `Anthropic.SDK` NuGet) |
| Market Data | Twelve Data REST API (daily + weekly OHLC) |
| Indicators | RSI (Wilder's smoothing), Floor Pivot Points |
| Frontend | Vanilla HTML/CSS/JS — no build step, dark theme |
| Testing | xUnit + FluentAssertions (21 unit tests) |
| Language | C# 12 / .NET 8 |

---

## 📄 License

MIT
