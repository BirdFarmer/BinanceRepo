# RSI Divergence Strategy Summary

## Strategy Logic
The RSI Divergence Strategy seeks to identify and act on bullish or bearish divergences in the market. It combines the following indicators:
1. **Relative Strength Index (RSI)** - Measures the strength of price movements, using a period of **20** for smoother trend identification.
2. **Stochastic Oscillator** - Used to confirm overbought and oversold conditions. The strategy takes action when the Stochastic dips below 10 (oversold) or rises above 90 (overbought).

### Entry Criteria:
  - RSI shows a bullish divergence, with the latest RSI higher than a previous low but the price making a new low.
  - RSI shows a bullish divergence: later price low is lower but RSI at that low is higher than the earlier low (we detect two lows and compare RSI).
    - After divergence, the strategy requires a "turn" candle before entering long. A turn candle is either:
      - a bullish candle with a lower wick at least as large as its body (wick >= body), OR
      - a small breakout above the immediate previous high (close > prevHigh * (1 + MinBreakPct)).
    - Optional: volume above recent average is used as an additional filter in historical runs.
  - RSI shows a bearish divergence: later price high is higher but RSI at that high is lower than the earlier high.
    - After divergence, the strategy requires a symmetric "turn" candle before entering short. A bearish turn is either:
      - a bearish candle with an upper wick at least as large as its body (upper wick >= body), OR
      - a small breakdown below the immediate previous low (close < prevLow * (1 - MinBreakPct)).

### Exit Criteria:

### Special Condition:
  - A dip below 20 will reset the "highest" RSI and price data for bearish signals.
  - A rise above 80 will reset the "lowest" RSI and price data for bullish signals.

## Optimal Market Conditions

## Limitations

**Note**: Consider parameterizing RSI length or Stochastic thresholds to adapt to different market conditions.

## Implementation Notes (reflects `RsiDivergenceStrategy.cs`)

- **RSI period:** 20 (uses `Indicator.GetRsi(quotes, 20)`).
- **Stochastic:** computed with `GetStoch(quotes, 100, 3, 3)` — a long %K length (100) and smoothing (3,3).
- **Thresholds:** Stochastic K <= 10 for bullish confirmation, >= 90 for bearish confirmation.
- **Divergence lookback:** default `lookback = 170` in the divergence detection methods.
 - **Entry lookahead:** when divergence is detected in historical replay the strategy scans up to `EntryLookaheadBars = 6` bars after the pivot to find the first qualifying turn candle; in live runs it requires the latest (signal) candle to qualify immediately.
 - **Candle body check / turn-candle rule:** bullish requires `Close > Open` and (lower wick >= body OR breakout above previous high); bearish requires `Close < Open` and (upper wick >= body OR breakdown below previous low).
- **Closed-candle policy:** indicators are computed on `workingKlines` which exclude the forming candle when `UseClosedCandles` is true.
- **Signal selection:** the signal and previous candle pair are selected from the same `workingKlines` used to compute indicators (so indicator indexes align with the chosen signal candles).
- **Entry actions:** calls `OrderManager.PlaceLongOrderAsync` or `PlaceShortOrderAsync` with the signal candle price and uses the signal candle `CloseTime` as the event timestamp; logs with `TraceSignalCandle` and `LogTradeSignal`.
- **Historical runner parity:** `RunOnHistoricalDataAsync` replays the same logic per-bar (it builds aligned subsets of `klines`, `rsiResults`, and `stochasticResults`) so historical signals and live signals are selected using the same indexing/anchoring rules.
 - **Historical / Live parity:** both `RunAsync` (live) and `RunOnHistoricalDataAsync` (historical replay) use the same entry logic: divergence detection followed by the symmetric turn-candle test. Historical runs scan ahead up to `EntryLookaheadBars` and place the entry at the first qualifying candle; live runs require the current candle to qualify immediately.

### Tunable defaults implemented in code
- `EntryLookaheadBars = 6` (how many bars after pivot to scan in historical runs)
- `MinBreakPct = 0.001` (0.1% breakout/breakdown tolerance)
- `VolumeLookback = 20`, `VolumeMultiplier = 1.0` (used to compute optional volume baseline for historical checks)
- `SmallValue = 1e-6` (used to avoid division by zero and to be tolerant of very low-priced symbols)

## Pine Script (approximation)

The following Pine Script (TradingView, Pine v5) is an approximate translation of the on-chain divergence logic. Pine's divergence detection APIs differ from the custom C# approach, so this script uses pivot highs/lows to approximate divergence points and applies the same RSI/stochastic thresholds and candle-body checks.

```pinescript
// @version=5
indicator("RSI Divergence (approx)", overlay=true)

// Parameters that mirror the C# values
rsiLen = input.int(20, "RSI Length")
stochLen = input.int(100, "Stoch Length")
kSmooth = input.int(3, "K Smooth")
dSmooth = input.int(3, "D Smooth")
pivotLeft = input.int(5, "Pivot Left")
pivotRight = input.int(5, "Pivot Right")


// Indicators
rsi = ta.rsi(close, rsiLen)
highestHigh = ta.highest(high, stochLen)
lowestLow = ta.lowest(low, stochLen)
rawK = (close - lowestLow) / math.max(highestHigh - lowestLow, 0.000001) * 100
k = ta.sma(rawK, kSmooth)
d = ta.sma(k, dSmooth)

// Pivot detection (approximate divergence anchors)
ph = ta.pivothigh(high, pivotLeft, pivotRight)
pl = ta.pivotlow(low, pivotLeft, pivotRight)

// Capture last pivot price and RSI at pivot index safely
var float ph_price = na
var float ph_rsi = na
if not na(ph)
    // pivot is pivotRight bars ago
    idx = pivotRight
    if bar_index > idx
        ph_price := ph
        ph_rsi := rsi[idx]

var float pl_price = na
var float pl_rsi = na
if not na(pl)
    idx2 = pivotRight
    if bar_index > idx2
        pl_price := pl
        pl_rsi := rsi[idx2]

// Simple divergence checks (require pivot + confirming candle body + stochastic threshold)
bearDiv = false
bullDiv = false
// Bearish: price makes higher high but RSI makes lower high
if not na(ph_price) and high > ph_price and rsi < ph_rsi and ph_rsi >= 80 and close < open and k >= 90
    bearDiv := true

// Bullish: price makes lower low but RSI makes higher low
if not na(pl_price) and low < pl_price and rsi > pl_rsi and pl_rsi <= 20 and close > open and k >= 90
    bullDiv := true

// Background color for signals (red for bearish, green for bullish)
bgcolor(bearDiv ? color.new(color.red, 85) : na, title="Bearish Background")
bgcolor(bullDiv ? color.new(color.green, 85) : na, title="Bullish Background")

// Alerts
alertcondition(bearDiv, title='Bearish Divergence', message='Bearish RSI divergence + Stoch>=90')
alertcondition(bullDiv, title='Bullish Divergence', message='Bullish RSI divergence + Stoch<=10')
```

*Notes:* This Pine Script is an approximation: C# code searches a longer lookback and selects signal/previous kline pairs explicitly. Use this as a starting point when exploring signals on TradingView.
