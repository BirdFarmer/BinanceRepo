# DEMA SuperTrend Strategy — Summary

This document summarizes the `DemaSupertrendStrategy` used in the project, highlights how the entry logic works in both live async and historical modes, and provides a TradingView Pine Script (v5) to visualize the indicator and entry points on a chart.

**Parameters**
- DEMA Length: 9
- Supertrend ATR Length: 2
- Supertrend ATR Multiplier: 3.35

**Where the code lives**
- Strategy implementation: `BinanceTestnet/Strategies/DemaSupertrendStrategy.cs`

**High-level description**
- The strategy computes a DEMA (Double EMA) of the close price and a Supertrend derived from that DEMA using ATR-based bands.
- A long entry is signalled when the Supertrend direction turns bullish (direction > 0 and previousDirection <= 0).
- A short entry is signalled when the Supertrend direction turns bearish (direction < 0 and previousDirection >= 0).
- The code has two modes:
  - `RunAsync` — runs against recent klines pulled from the API and uses the latest result pair to decide entry immediately.
  - `RunOnHistoricalDataAsync` — iterates through historical candles and emits entries for each bar where direction flips.

**Are the entry conditions identical?**
- Functional condition used to decide entries is the same in both methods: a flip in the `Direction` value between `previousResult` and `currentResult` triggers an entry.
  - Long entry: `current.Direction > 0 && previous.Direction <= 0`
  - Short entry: `current.Direction < 0 && previous.Direction >= 0`

**Implementation differences to be aware of**
- `RunAsync`: selects the latest two klines by index (last and previous) to match `RunOnHistoricalDataAsync` mapping, and uses those as the signal/previous candles when placing orders. The DEMA/Supertrend results are taken from the last two items in the computed results list.
- `RunOnHistoricalDataAsync`: iterates by index through `historicalCandles` and accesses `demaSupertrendResults[i]` and `[i-1]` directly. It then places orders using the `currentKline` (the candle at index `i`) as the signal candle.

Because both methods compare the same computed `Direction` values, and because the DEMA/Supertrend results list is built to align 1:1 with the provided quotes (one `DemaSupertrendResult` per quote), the logical entry conditions are effectively the same. However, small differences in which candle is considered the "signal" candle can occur depending on how `SelectSignalPair` chooses the signal (e.g., if it uses closed vs. last partial candle). Use the `EnableDebug` prints added in `DemaSupertrendStrategy.cs` to compare what `signalKline` (async) vs. `currentKline` (historical) values are used at the time of a flip.

**Potential pitfalls / suggestions**
- Indicator alignment: confirm how `Indicator.GetDema` and `Indicator.GetAtr` align their outputs vs. the input `quotes`. Note: the implementation now aligns `demaResults[i]` and `atrResults[i]` with `quotes[i]` (the previous `i - period` offsets were removed), which prevents the ~30-bar late signals that can happen when indicator arrays are misaligned. If you still see offsets vs. the chart, inspect the `demaResults` and `atrResults` arrays and compare their `Date` fields to `quotes`.
- `SelectSignalPair` behavior: if it sometimes returns the penultimate candle rather than the last closed candle, that explains apparent mismatches when comparing to your chart. The debug prints show the `CloseTime` and close price for both `signalKline` and `previousKline` when a trade is about to be placed.
- If you want absolute parity between live and historical behavior, either: (a) make `RunAsync` use the same index-based mapping as the historical method (i.e., use the last result at index `Count - 1`), or (b) modify `RunOnHistoricalDataAsync` to use `SelectSignalPair` logic when defining the signal bar.

**How to debug further**
- Enable/disable `EnableDebug` in `DemaSupertrendStrategy.cs` to control console output.
- Add extra prints showing `demaResults[i].Date` and `atrResults[i].Date` to verify indicator alignment.

---

## Pine Script (TradingView v5)

Paste the following into TradingView's Pine editor to visualize DEMA Supertrend and background fills on entries:

```pinescript
//@version=5
indicator("DEMA Supertrend (DEMA center)", overlay=true)

// User inputs
dema_len = input.int(9, "DEMA Length")
atr_len = input.int(2, "ATR Length")
mult = input.float(3.35, "ATR Multiplier")

src = close

// DEMA calculation
ema1 = ta.ema(src, dema_len)
ema2 = ta.ema(ema1, dema_len)
dema = 2 * ema1 - ema2

// ATR
atr = ta.atr(atr_len)

// Raw bands
upperBand = dema + mult * atr
lowerBand = dema - mult * atr

// Adjusted (final) bands using previous final bands (recursive)
var float finalUpper = na
var float finalLower = na

if bar_index == 0
    finalUpper := upperBand
    finalLower := lowerBand
else
    prevFinalUpper = nz(finalUpper[1])
    prevFinalLower = nz(finalLower[1])
    adjustedLower = lowerBand
    adjustedUpper = upperBand
    if not na(prevFinalLower) and not na(prevFinalUpper)
        adjustedLower := (lowerBand > prevFinalLower or close[1] < prevFinalLower) ? lowerBand : prevFinalLower
        adjustedUpper := (upperBand < prevFinalUpper or close[1] > prevFinalUpper) ? upperBand : prevFinalUpper
    finalUpper := adjustedUpper
    finalLower := adjustedLower

// Supertrend direction and value
var int dir = 1
var float superValue = na
if bar_index == 0
    dir := 1
    superValue := finalLower
else
    prevSuper = nz(superValue[1])
    prevFinalUpper = nz(finalUpper[1])
    if na(prevSuper)
        dir := 1
    else if prevSuper == prevFinalUpper
        dir := close > finalUpper ? -1 : 1
    else
        dir := close < finalLower ? 1 : -1
    superValue := dir == -1 ? finalLower : finalUpper

// Plot DEMA and Supertrend
plot(dema, color=color.orange, title="DEMA")
plot(superValue, title="Supertrend", color=(dir == 1 ? color.red : color.green), linewidth=2)

// Entry detection (direction flip)
longEntry = (dir == 1) and (nz(dir[1], 0) <= 0)
shortEntry = (dir == -1) and (nz(dir[1], 0) >= 0)

// Background fill on entries
bgcolor(longEntry ? color.new(color.red, 80) : shortEntry ? color.new(color.green, 80) : na)

// Optional shapes
// plotshape(longEntry, title="Short", location=location.bottom, color=color.red, style=shape.triangleup, size=size.tiny)
// plotshape(shortEntry, title="Long", location=location.top, color=color.green, style=shape.triangledown, size=size.tiny)

// Labels for debugging (toggle off if noisy)
//label.new(bar_index, high, tostring(dir), yloc=yloc.abovebar, color=color.gray, textcolor=color.white, style=label.style_label_left)
```

---

If you'd like, I can:
- Add a unit test that runs `RunOnHistoricalDataAsync` on a small known dataset and asserts the same entry indices as the Pine Script.
- Modify `RunAsync` to use the same indexing method as the historical runner (or vice-versa) for guaranteed parity.
- Output indicator arrays (dates + DEMA + ATR + final bands + direction) to CSV for side-by-side comparison with the chart.

Tell me which of those you'd like next and I will proceed.