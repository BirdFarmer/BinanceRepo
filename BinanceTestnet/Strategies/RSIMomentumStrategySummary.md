# RSI Momentum Strategy Summary

## Strategy Logic
The RSI Momentum strategy monitors RSI momentum and uses a lightweight state machine to wait for strong momentum shifts before entering trades.

- **RSI period:** 14 (internal `_rsiPeriod = 14`).
- **Lookback / data request:** requests `_lookbackPeriod + 1` candles where `_lookbackPeriod` is `150`.
- **Closed-candle policy:** `SupportsClosedCandles => true` and the strategy evaluates indicators on `workingKlines` (it excludes the forming candle when `UseClosedCandles` is enabled).
- **State machine:** the strategy tracks a state per-symbol (stored in `rsiStateMap`) with values like `OVERBOUGHT`, `OVERSOLD`, and `NEUTRAL`.
- **Thresholds:** the code uses 29 and 71 as the key levels:
  - Transition to a "coming from oversold" condition when RSI crosses from below 29 to >= 29.
  - Transition to a "coming from overbought" condition when RSI crosses from above 71 to <= 71.

## Entry & Initialization Logic (reflects `RSIMomentumStrategy.cs`)

- **Initialization:** `InitializeRSIState` scans historical RSI values from latest to earliest and sets the initial `rsiStateMap[symbol]` to `OVERBOUGHT` if it finds an RSI >= 71, to `OVERSOLD` if it finds an RSI <= 29, otherwise `NEUTRAL`.

- **Realtime evaluation:** `EvaluateRSIConditions` compares the previous and current RSI values (it requires at least two RSI values). It enforces a lock to avoid concurrent state races.

- **Long entry:** If current state is `OVERSOLD` and RSI moves from below 71 to >= 71 (i.e., `previousRsi < 71 && currentRsi >= 71`), the strategy places a LONG via `OrderManager.PlaceLongOrderAsync` and sets the symbol state to `OVERBOUGHT`.

- **Short entry:** If current state is `OVERBOUGHT` and RSI moves from above 29 to <= 29 (i.e., `previousRsi > 29 && currentRsi <= 29`), the strategy places a SHORT via `OrderManager.PlaceShortOrderAsync` and sets the symbol state to `OVERSOLD`.

- **Historical replay:** `RunOnHistoricalDataAsync` implements equivalent logic for simulated/backtest runs. It uses a per-run `rsiStateMap` and places orders via `OrderManager.PlaceLongOrderAsync` and `PlaceShortOrderAsync` when the same crossing conditions occur.

- **Trade lifecycle:** After potential entries, the code calls `OrderManager.CheckAndCloseTrades` to evaluate exits/closures.

## Other implementation details

- `FetchRSIFromKlinesAsync` converts klines to `Quote` and calls `Indicator.GetRsi(quotes, _rsiPeriod)` and returns the numeric RSI values.
- Concurrency: `EvaluateRSIConditions` uses a private `_lock` object to avoid race conditions while reading/updating shared state maps.

## Pine Script (approximation)

This TradingView Pine v5 script approximates the state-machine and entry rules used by `RSIMomentumStrategy`. It is not a 1:1 match for multi-symbol maps or the async trade placement framework, but it encodes the same thresholds and entry behavior.

```pinescript
// @version=5
indicator("RSI Momentum (approx)", overlay=true)

len = input.int(14, "RSI Length")
oversold = input.int(29, "Oversold Threshold")
overbought = input.int(71, "Overbought Threshold")

// Confirmation inputs
fastRsiLen = input.int(7, "Fast RSI Length")
fastRsiLong = input.int(65, "Fast RSI Long Threshold")
fastRsiShort = input.int(35, "Fast RSI Short Threshold")
volLookback = input.int(20, "Volume Lookback")
volMultiplier = input.float(1.0, "Volume Multiplier", step=0.1)
cooldown = input.int(3, "Cooldown Candles")

rsi = ta.rsi(close, len)
prev_rsi = rsi[1]
fastRsi = ta.rsi(close, fastRsiLen)

// volume confirmation
avgVol = ta.sma(volume, volLookback)
volOk = nz(avgVol) == 0 ? true : volume >= avgVol * volMultiplier

// Persistent state variable (per-symbol in TradingView)
var string state = "NEUTRAL"

// Per-bar entry flags (true only on the bar the entry condition occurs)
var bool longSig = false
var bool shortSig = false
longSig := false
shortSig := false
// track last entry bar to enforce cooldown
var int lastEntryBar = na

// Detect coming-from states (mirrors RunOnHistoricalDataAsync behavior)
if prev_rsi < oversold and rsi >= oversold
    state := "COMING_FROM_OVERSOLD"
else if prev_rsi > overbought and rsi <= overbought
    state := "COMING_FROM_OVERBOUGHT"

// Entries (mirrors EvaluateRSIConditions) — set per-bar signal flags
if state == "COMING_FROM_OVERSOLD" and rsi >= overbought
    // require fast RSI and volume confirmation and cooldown
    if fastRsi >= fastRsiLong and volOk and (na(lastEntryBar) or (bar_index - lastEntryBar) >= cooldown)
        longSig := true
        state := "OVERBOUGHT"
        lastEntryBar := bar_index

if state == "COMING_FROM_OVERBOUGHT" and rsi <= oversold
    if fastRsi <= fastRsiShort and volOk and (na(lastEntryBar) or (bar_index - lastEntryBar) >= cooldown)
        shortSig := true
        state := "OVERSOLD"
        lastEntryBar := bar_index

// Background color for signals (green for long, red for short)
bgcolor(longSig ? color.new(color.green, 85) : na, title="Long Background")
bgcolor(shortSig ? color.new(color.red, 85) : na, title="Short Background")

// Optional: plot RSI and thresholds for visual validation
plot(rsi, title="RSI", color=color.blue)
hline(oversold, "Oversold", color=color.green)
hline(overbought, "Overbought", color=color.red)

// Alerts
alertcondition(longSig, title="RSI Momentum Long", message="RSI Momentum LONG signal")
alertcondition(shortSig, title="RSI Momentum Short", message="RSI Momentum SHORT signal")
```

Use this script to sanity-check signals in TradingView and iterate on thresholds or smoothing before applying changes back to the C# strategy.

## Recent Code Changes (confirmed behavior)

The implementation was recently updated to reduce false entries by adding parameterized confirmations and a cooldown. Key changes:

- **Fast RSI confirmation:** a fast RSI (`_fastRsiPeriod = 7`) is computed and used as additional confirmation before placing entries. Longs require `fastRsi >= _fastRsiLongThreshold` (default 65). Shorts require `fastRsi <= _fastRsiShortThreshold` (default 35).
- **Volume confirmation:** optional volume check compares the current candle volume to the average over `_volumeLookback` (default 20). The rule requires `volume >= avgVolume * _volumeMultiplier` (default multiplier = 1.0). This is applied to both live and historical runs.
- **Cooldown between entries:** per-symbol cooldown (`_cooldownCandles`, default 3) prevents immediate re-entries for the configured number of candles after an entry.
- **Parameter fields added in code:** `_fastRsiPeriod`, `_fastRsiLongThreshold`, `_fastRsiShortThreshold`, `_cooldownCandles`, `_volumeMultiplier`, `_volumeLookback`, and `_lastEntryIndexMap` to track last entry indexes.
- **Historical runner parity:** `RunOnHistoricalDataAsync` now computes fast RSI and applies the same confirmation and cooldown logic when emitting historical signals, so backtests match live behavior more closely.

If you'd like, I can add these parameters to a `StrategySettings` object or expose them via constructors so they are easier to tune in backtests.
