# MultiTesterWindow & Multi-Backtest Harness Usage Guide

## Overview
The MultiTesterWindow is a WPF desktop UI for running batch backtests on multiple strategies, symbols, and configurations using a JSON config file. It automates historical data fetching, runs combinations, and outputs results to CSV with summary metrics.

## How to Use

### 1. Open MultiTesterWindow
- In the TradingAppDesktop, click the "Multi Tester" button (usually in the main window or navigation panel).
- The MultiTesterWindow will open, showing config/database selectors, run/cancel buttons, progress, summary, and a JSON config editor.

### 2. Select Config & Database
- **Config File (JSON):** Browse or enter the path to your multi-backtest config (e.g., `multi_backtest.sample.json`).
- **Database Path:** Browse or enter the path to your SQLite trading database (e.g., `TradingData.db`).

### 3. Edit JSON Config (Optional)
- Use the built-in JSON editor to view or modify the config file.
- Click **Reload** to refresh from disk, **Validate** to check JSON syntax, and **Save** to write changes.

### 4. Run Backtests
- Click **Run Backtests** to start batch testing.
- Progress will be shown in the right panel.
- After completion, a summary of the latest run appears in the summary section.
- Results are saved to `results/multi/multi_results.csv` (relative to config location).
 - Click **Run Backtests** to start batch testing.
 - Progress will be shown in the right panel; each progress entry is selectable so you can copy lines if needed.
 - After completion, a rich "Best Run" summary appears in the summary section. The summary includes the run start datetime (when available), the run end datetime (`endUtc`) and the number of candles tested (conservative count), plus a short recommendations block.
 - There's a **Copy Insights** button in the summary panel to copy the full summary to clipboard.
 - Results are saved to `results/multi/multi_results.csv` (relative to config location). The CSV now includes `strategy` and `startUtc` (when available) so runs are self-describing.

### 5. View Results
- Click **Open Results Folder** to open the output directory in Explorer.
- The CSV contains detailed metrics for each run (win rate, netPnL, expectancy, top/bottom symbols, etc).
 - Click **Open Results Folder** to open the output directory in Explorer.
 - The CSV contains detailed metrics for each run (win rate, netPnL, expectancy, top/bottom symbols, etc). Newer runs also include `strategy`, `startUtc`, `endUtc` and `candlesTested` columns when that data is available.
 - `candlesTested` is a conservative integer: for multi-symbol runs it represents the minimum number of candles fetched across symbols (so the value is valid for every symbol); for single-symbol runs it equals the number of klines fetched.
 - The runner performs a single-page fetch per symbol (one API call requesting up to `BatchSize` klines — default 1500). The implementation does not page across multiple requests. If you need longer history, increase `BatchSize` up to the API limit or run with a later `StartUtc`.
 - The runner writes a small `strategy_insights.json` into the same `results/multi` folder containing a concise "best setup" string per strategy (used by the desktop UI). Tooltips are informational only; the previous "Apply Best Setup" action was removed to avoid misleading automatic UI changes.

## JSON Config Structure & Editing

The config file controls all batch parameters. Example:
```json
{
  "Strategy": "MACDStandard",
  "Timeframes": ["5m", "15m", "1h"],
  "SymbolSets": {
    "tier_1_5": ["BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT"],
    "tier_6_10": ["ADAUSDT", "DOGEUSDT", "TRXUSDT", "TONUSDT", "AVAXUSDT"]
  },
  "ExitModes": [
    {
      "Name": "fixed",
      "Trailing": false,
      "RiskProfiles": [
        { "Name": "rr_1_to_1", "TpMultiplier": 1.0, "SlMultiplier": 1.0 }
      ]
    },
    {
      "Name": "trailing_default",
      "Trailing": true,
      "ActivationPct": 0.8,
      "CallbackPct": 0.35
    }
  ],
  "Output": {
    "Directory": "results/multi",
    "Format": "csv"
  },
  "Historical": {
    "StartUtc": "2025-10-01T00:00:00Z",
    "BatchSize": 1500,
    "MaxCandles": 1500
  }
}
```

### How to Change/Add/Remove Parameters
- **Strategy:** Change to any supported strategy name below:

  - `EmaStochRsi` (EMA + Stoch RSI)
  - `EnhancedMACD` (Enhanced MACD)
  - `FVG` (Fair Value Gap)
  - `IchimokuCloud` (Ichimoku Cloud)
  - `RSIMomentum` (RSI Momentum)
  - `MACDStandard` (Standard MACD)
  - `RsiDivergence` (RSI Divergence)
  - `FibonacciRetracement` (Fibonacci)
  - `Aroon` (Aroon Oscillator)
  - `HullSMA` (Hull SMA)
  - `SMAExpansion` (SMA Expansion)
  - `SimpleSMA375` (Simple SMA 375)
  - `BollingerSqueeze` (Bollinger Squeeze)
  - `SupportResistance` (Support Resistance Break)

- **Timeframes:** Add/remove intervals (e.g., `"5m"`, `"1h"`).
- **SymbolSets:** Add new sets or symbols, remove unwanted ones, or rename sets.
- **ExitModes:**
  - Add new exit mode blocks for different risk profiles or trailing settings.
  - Edit `RiskProfiles` for fixed TP/SL ratios.
  - Edit `ActivationPct`/`CallbackPct` for trailing stops.
- **Output:** Change output directory or format (currently only CSV supported).
- **Historical:**
  - Change `StartUtc` for different historical start dates.
  - Adjust `BatchSize` or `MaxCandles` for data fetch limits.

#### Example: Add a New Symbol Set
```json
"SymbolSets": {
  "tier_1_5": ["BTCUSDT", "ETHUSDT"],
  "tier_6_10": ["ADAUSDT", "DOGEUSDT"],
  "new_set": ["LTCUSDT", "LINKUSDT"]
}
```

#### Example: Add a New Timeframe
```json
"Timeframes": ["5m", "15m", "1h", "4h"]
```

#### Example: Add a New Exit Mode
```json
"ExitModes": [
  ...existing modes...,
  {
    "Name": "aggressive_trailing",
    "Trailing": true,
    "ActivationPct": 0.5,
    "CallbackPct": 0.2
  }
]
```

#### Example: Remove a Parameter
- To remove a symbol, timeframe, or exit mode, simply delete its entry from the array or object.

### Validation
- Use the **Validate** button in the MultiTesterWindow to check for JSON syntax errors before saving.
- Ensure all required fields are present (see sample above).

## Tips
- Always validate your config after editing.
- Output CSV is overwritten/appended for each run; back up if needed.
- You can run with different configs by browsing/selecting a new JSON file.

## Per-strategy insights & main UI tooltips

- After a run completes the MultiTester extracts a concise "best setup" for the top-performing run and writes it to `results/multi/strategy_insights.json` next to the CSV.
- The TradingAppDesktop main window will look for this file (in its own results folder or across the repository) and automatically apply per-strategy short recommendations as tooltips in the strategy selector.
Tooltips show the concrete setup that produced the best run (timeframe, symbol set or expanded symbol list if available, TP/SL multipliers, win rate, net PnL, trades, start/end/candles when present). Tooltips are informational only — the UI will not automatically apply configurations from the tooltip.

## Notes on summary & reliability

- The summary presents a best-run focused view and short recommendations. Treat single best-run results cautiously if sample sizes are small — check the trade count and aggregated runs.
- Expectancy and payoff are shown in the summary; expectancy is the expected profit per trade (p * avgWin − (1−p) * avgLoss) and payoff is avgWin/avgLoss.

## Troubleshooting
- If you see errors about missing controls or build issues, clean and rebuild your solution.
- For API/data errors, check your database path and symbol names.
- For more help, see inline comments in the sample config or ask for support.

---
For advanced usage, you can edit the config directly in the app, or externally in any text editor. The MultiTesterWindow is designed for rapid batch testing and strategy comparison.
