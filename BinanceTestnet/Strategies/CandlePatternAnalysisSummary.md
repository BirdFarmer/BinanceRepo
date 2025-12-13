# Candle Pattern Analysis Strategy — Summary

Location: `BinanceTestnet/Strategies/CandlePatternAnalysisStrategy.cs`

This document explains the Candle Pattern Analysis strategy: its entry conditions, optional filters, runtime behavior, UI knobs, logging, and a PineScript reference so the user can reproduce/visualize the pattern on TradingView.

## Overview

The Candle Pattern Analysis strategy looks for short sequences of "indecisive" candles (small body relative to the candle range) followed by a decisive breakout candle that closes outside the indecisive range. Optionally the breakout must be accompanied by a volume breakout and/or an EMA trend filter.

This is derived from a PineScript prototype. The C# implementation is in `CandlePatternAnalysisStrategy.cs` and supports both live (`RunAsync`) and historical/backtest (`RunOnHistoricalDataAsync`) runs.

## Entry Conditions (core)

- Indecisive candle: body size < `IndecisiveThreshold` × total range
  - body = |close - open|
  - range = high - low
  - default `IndecisiveThreshold = 0.3` (30%)
- Pattern: three consecutive indecisive candles followed by a non-indecisive candle (3 + 1 pattern)
- Range calculation: `range_high` = highest high of the three indecisive candles; `range_low` = lowest low of the three indecisive candles
- Signal:
  - Bullish: current candle close > `range_high`
  - Bearish: current candle close < `range_low`

These are implemented in the helper `TryDetectSignal(...)` which is also used by unit tests.

## Optional Filters

- Volume Filter (default: enabled)
  - Require current candle volume > SMA(volume, `VolumeMALength`)
  - Default `VolumeMALength = 20`
- EMA Trend Filter (default: disabled)
  - Bull only when price > EMA(`EmaLength`) and Bear only when price < EMA(`EmaLength`)
  - Default `EmaLength = 50`

All filters are applied after the core pattern detection. Both filters are configurable at runtime via the Settings UI and as static fields on the strategy class.

## Strategy Parameters (runtime / UI)

- `UseVolumeFilter` (bool) — default: true
- `UseEmaFilter` (bool) — default: false
- `IndecisiveThreshold` (decimal) — default: `0.3m` (30%)
- `VolumeMALength` (int) — default: `20`
- `EmaLength` (int) — default: `50`
- `CandleFetchBuffer` (int) — default: `150` (extra historical bars requested for warm-up)
- `DebugMode` (bool) — default: false; toggles verbose runtime logging

The Settings UI exposes these controls under Settings → Strategies → Candle Pattern Analysis.

## Live vs Historical behavior

- RunOnHistoricalDataAsync (backtest): iterates through all historical candles and evaluates `TryDetectSignal` for each closed candle (i = 3..N-1), so behavior matches the PineScript backtest when closed bars are evaluated.
- RunAsync (live): fetches a limited window of recent klines and uses `SelectSignalPair(...)` (StrategyBase helper) to choose the candidate signal kline according to the app's `UseClosedCandles` runtime policy. If closed-candle mode is disabled, the strategy may evaluate the forming / last candle instead of the previous closed candle — this can cause behavior differences versus the historical run. To match historical output, enable closed-candle mode in the UI (MainWindow → `UseClosedCandles`) or set `Helpers.StrategyRuntimeConfig.UseClosedCandles = true`.

## Logging

The strategy prints clear structured logs for signals. Example output:

```
****** Candle Pattern Breakout ******************
LONG: BTCUSDT @ 50000 (Broken above 49850 range)
Volume: 25000 > 20000×1.0
EMA50: 49500 (Price > EMA: ✓)
************************************************
```

When `DebugMode` is enabled, additional tracing is written showing:
- how many candles were requested (`limit`)
- which index was selected as the signal kline
- indecisive checks and body/range values for the last few bars
- volume and EMA checks and whether they passed

Example debug lines added to the strategy:

- `[CandlePattern] requesting {limit} klines (required={required}, buffer={buffer})`
- `[CandlePattern] signalKline index={idx}, time=...`
- `[CandlePattern] TryDetectSignal => BULL|BEAR (rangeHigh={rangeHigh}, rangeLow={rangeLow}, close={close})`
- `[CandlePattern] Volume check: currVol=..., avgVol(...)=..., volOk=...`
- `[CandlePattern] EMA check: EMA{EmaLength}=..., price=..., emaOk=...`

## Unit tests

- `BinanceTestnet.UnitTests/CandlePatternAnalysisStrategyTests.cs` includes synthetic tests that exercise bullish and bearish detection and a negative case. The core helper `TryDetectSignal` is used for deterministic checks.

## PineScript reference

The following PineScript is the prototype reference. You can paste this into TradingView to visualize the pattern and validate signals.

```pinescript
// Core pattern detection
is_indec = math.abs(close - open) / (high - low) < 0.3
pattern = not is_indec and is_indec[1] and is_indec[2] and is_indec[3]
range_high = math.max(high[1], math.max(high[2], high[3]))
range_low = math.min(low[1], math.min(low[2], low[3]))
bear_signal = pattern and close < range_low
bull_signal = pattern and close > range_high

plotshape(bull_signal, title="Bull Break", location=location.abovebar, color=color.green, style=shape.triangleup, size=size.small)
plotshape(bear_signal, title="Bear Break", location=location.belowbar, color=color.red, style=shape.triangledown, size=size.small)
```

Adjust the threshold `0.3` in the script to match `IndecisiveThreshold` in the C# strategy for visual parity.

## Integration notes

- The strategy calls into `OrderManager.PlaceLongOrderAsync(...)` and `PlaceShortOrderAsync(...)` for entry execution and uses `OrderManager.CheckAndCloseTrades(...)` to evaluate exits.
- It respects the app's `UseClosedCandles` runtime policy via the `StrategyBase` helper `SelectSignalPair(...)`.
- The strategy is designed to run concurrently across multiple symbols and is lightweight; the only non-trivial CPU load comes from the EMA calculation (Skender.Stock.Indicators) when `UseEmaFilter` is enabled.

## Troubleshooting tips

- If historical/backtest results look different from live:
  - Ensure `UseClosedCandles` is enabled for live runs to compare closed-bar behavior.
  - Increase `CandleFetchBuffer` if you change to very large EMA lengths (e.g., 200).
  - Enable `DebugMode` from Settings → Candle Pattern Analysis to see step-by-step checks.

## Changelog

- v1.0: Initial implementation of Candle Pattern Analysis strategy and UI controls.

---
Generated automatically by the development toolchain. For code edits refer to `CandlePatternAnalysisStrategy.cs`.
