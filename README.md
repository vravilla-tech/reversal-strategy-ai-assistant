# 📉 FX SHORT Reversal Strategy — AI Assistant

An FX trading signal application that detects **SHORT reversal setups** using a 4-step rules-based strategy engine, combined with an AI narrative layer powered by [Claude](https://claude.ai) by Anthropic.

> Built by a senior .NET developer exploring AI engineering — demonstrating how deterministic quant logic and large language models work together in a production-style .NET 8 application.

---

## 🧠 Strategy Logic

The app evaluates a **4-step SHORT Reversal Strategy** on FX pairs using daily and weekly candles:

| Step | Timeframe | Rule | Detail |
|------|-----------|------|--------|
| **1** | Daily | **RSI(14) breaks above 70** | RSI was below 70, now crosses above — overbought breakout signal |
| **2** | Daily | **Price reaches pivot resistance + bearish rejection** | Candle wick touches R1/R2/R3 intrabar, closes below it — shooting-star/bearish rejection |
| **3** | Weekly | **Resistance tested 2+ times in last 3–5 weeks** | Switch to weekly candles — confirms the level is a known, tested resistance |
| **4** | Daily | **Latest completed daily candle is bearish** | Close < open on the last finished daily candle — confirms downward pressure |
| ✅ | — | **Signal: SHORT** | All 4 rules met at daily close |

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
│       │   ├── IndicatorEngine.cs               # RSI (Wilder), pivot points, bearish-rejection checks, weekly test count
│       │   ├── ReversalStrategyEngine.cs        # Applies 4-step rule chain, returns SignalResult
│       │   └── ClaudeExplainerService.cs        # Structured prompt → Claude → analyst narrative
│       ├── Models/
│       │   ├── Candle.cs                        # OHLCV record
│       │   ├── PivotLevels.cs                   # P, R1-R3, S1-S3
│       │   └── SignalResult.cs                  # Full evaluation result (all rule flags + narrative)
│       └── wwwroot/index.html                   # Dark-theme SPA dashboard
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
- **Cost-aware Claude usage** — Claude is only called when at least Rule 1 (RSI breakout) is satisfied
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
| Language | C# 12 / .NET 8 |

---

## 📄 License

MIT
