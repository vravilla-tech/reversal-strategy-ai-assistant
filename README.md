# 📈 Reversal Strategy AI Assistant

An FX trading signal application that combines a rules-based technical analysis engine with an AI-powered narrative layer using [Claude](https://claude.ai) by Anthropic.

> Built by a .NET developer exploring AI engineering — demonstrating how deterministic quant logic and large language models can work together in a production-style .NET 8 application.

---

## 🧠 What it does

The app evaluates a **Reversal Strategy** for FX pairs by walking through a rule chain at the daily market close:

| Step | Rule | Detail |
|------|------|--------|
| 1 | **RSI Condition** | RSI (14-period) below 30 = oversold → potential BUY reversal |
| 2 | **Pivot Point Alignment** | Price must be below the daily pivot point (confirms bearish pressure before reversal) |
| 3 | **Weekly S/R Intrabar Touch** | Today's candle High/Low must have touched or crossed Weekly S2 or S3 support levels |
| ✅ | **Signal** | All three conditions met at close → BUY signal flagged |

Claude then receives the full indicator data and produces a **plain-English analyst narrative** explaining the setup — like having a trading analyst on call.

---

## 🏗️ Architecture

```
reversal-strategy-ai-assistant/
├── src/
│   └── ReversalStrategy.Api/           # ASP.NET Core 8 Web API
│       ├── Controllers/
│       │   └── SignalsController.cs    # REST endpoints: /api/signals/{pair} & /api/signals/scan
│       ├── Services/
│       │   ├── MarketDataService.cs    # Fetches OHLC data from Twelve Data API
│       │   ├── IndicatorEngine.cs      # RSI (Wilder), Pivot Points (floor method), S/R touch logic
│       │   ├── ReversalStrategyEngine.cs # Applies the 3-rule reversal strategy chain
│       │   └── ClaudeExplainerService.cs # Sends signal data to Claude → returns analyst narrative
│       ├── Models/                     # Candle, PivotLevels, SignalResult records
│       └── wwwroot/index.html          # Single-page dashboard (vanilla JS, dark theme)
└── ReversalStrategy.sln
```

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Twelve Data API key](https://twelvedata.com) (free tier — 800 calls/day)
- [Anthropic API key](https://console.anthropic.com)

### 1. Clone the repo

```bash
git clone https://github.com/vravilla-tech/reversal-strategy-ai-assistant.git
cd reversal-strategy-ai-assistant
```

### 2. Add your API keys

Edit `src/ReversalStrategy.Api/appsettings.json`:

```json
{
  "TwelveData": {
    "ApiKey": "your_twelvedata_key_here"
  },
  "Anthropic": {
    "ApiKey": "your_anthropic_key_here"
  }
}
```

> ⚠️ Never commit real API keys. Use `appsettings.Development.json` (git-ignored) locally.

### 3. Run

```bash
cd src/ReversalStrategy.Api
dotnet run
```

Then open:
- **Dashboard:** http://localhost:5000
- **Swagger UI:** http://localhost:5000/swagger

---

## 📡 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/signals/{symbol}` | Evaluate a single FX pair (e.g. `EUR-USD`) |
| `GET` | `/api/signals/scan` | Scan all 7 major FX pairs |
| `GET` | `/health` | Health check |

---

## 🤖 AI Engineering Highlights

- **Structured prompting:** Claude receives a precise, templated data payload — not free-text — ensuring consistent, grounded responses
- **LLM as explainability layer:** The deterministic signal engine runs first; Claude's role is interpretation, not decision-making
- **Cost-aware design:** Claude is only called when the RSI condition is met (not for every pair on every scan)
- **Separation of concerns:** Strategy logic is fully testable without any LLM dependency

---

## 🛠️ Tech Stack

| Layer | Technology |
|-------|-----------|
| API | ASP.NET Core 8 Web API |
| AI | Claude (via `Anthropic.SDK` NuGet package) |
| Market Data | Twelve Data REST API |
| Frontend | Vanilla HTML/CSS/JS (dark theme, no build step) |
| Language | C# 12 / .NET 8 |

---

## 📄 License

MIT
