# BinanceAPI — Multi Backtest Harness

This repository contains a multi-strategy backtest harness and a desktop WPF UI (`TradingAppDesktop`) to run and analyze batch backtests.

## strategy_insights.json example

After a multi-backtest run the harness writes a compact per-strategy file `results/multi/strategy_insights.json` that the desktop UI reads and surfaces as tooltips in the strategy selector.

Example `strategy_insights.json`:

```json
{
  "MACDStandard": "Best setup — TF:1h; Symbols:tier_50_54 (BTCUSDT,ETHUSDT,BNBUSDT); TP:2.5; SL:2.0; Win:45.45% ; Net:37.80; Trades:24; Start:2025-10-01 00:00:00 UTC; Candles:1000",
  "IchimokuCloud": "Best setup — TF:4h; Symbols:tier_1_5 (BTCUSDT,ETHUSDT); TP:1.5; SL:1.0; Win:52.00% ; Net:120.45; Trades:18; Start:2025-09-01 00:00:00 UTC; Candles:800"
}
```

Each value is a compact human-readable string produced by the MultiTester summarizer. The desktop UI formats it into a multi-line tooltip and provides an "Apply Best Setup" action to copy the setup into the main UI (timeframe, symbols, TP/SL where possible).

Notes:
- The JSON is intentionally small and easy to parse. The string values use `;` separators and may include an expanded symbol list in parentheses.
- The desktop UI will search for `strategy_insights.json` under `results/multi` and apply the mappings automatically as tooltips.

If you want the UI to automatically *apply* the best setup on hover/click, enable the context-menu action or ask me to wire automatic application.
