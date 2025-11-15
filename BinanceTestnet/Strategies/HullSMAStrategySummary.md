## HullSMAStrategy — Implementation Summary

This file describes what `HullSMAStrategy.cs` currently implements and provides a TradingView Pine Script v5 indicator you can paste into TradingView to visualize the same signals.

**Core idea**: entries are generated when a short Hull moving average (HMA) crosses a long HMA, subject to filters: an RSI "boring zone" filter, a recent-volume vs average-volume filter, and an optional short-term momentum check.

Key parameters (current values in code):
- Short HMA length: 35
- Long HMA length: 100
- RSI period: 20; RSI boring zone: 40–60 (signals blocked when RSI is inside this range)
- Volume confirmation: enabled (`UseVolumeConfirmation = true`) with `MinVolumeRatio = 1.0m` (logic below)
- Momentum confirmation: enabled (`UseMomentumConfirmation = true`) with `MomentumPeriod = 5`
- Supports closed candles: true (strategy processes completed candles)

Volume confirmation details
- The strategy computes:
  - `recentVolume` = average(volume of last 5 candles)
  - `avgVolume` = average(volume of last 20 candles)
- The check is `recentVolume >= avgVolume * MinVolumeRatio`.
- With `MinVolumeRatio = 1.0`, the strategy will block trades where recent volume is below the 20-sample average (i.e., it does not allow trades on below-average volume). A value >1 requires a spike; <1 is more permissive.

Signal and execution flow (live/backtest)
- For each symbol the strategy:
  1. Fetches klines (limit 210) and converts them to indicator `Quote`s.
  2. Computes RSI and Hull (EHMA) results for short and long lengths.
  3. Selects a signal pair (current and previous kline) per the strategy base policy.
  4. Applies RSI and volume filters; if either blocks, the signal is ignored.
  5. If filters pass, evaluates Hull crossover conditions:
     - Long: short.EHMA_current > long.EHMA_current AND short.EHMA_prev <= long.EHMA_prev AND long.EHMA_current > long.EHMA_prev
     - Short: symmetrical check for crossing down
  6. If crossover true, optional momentum confirmation is evaluated (momentumPercent over `MomentumPeriod` >0 for long, <0 for short).
  7. If all checks pass, places a long/short order via `OrderManager.PlaceLongOrderAsync` / `PlaceShortOrderAsync` using the signal kline close time as the trade timestamp.

Diagnostics and debugging
- The strategy maintains per-run counters (symbols processed, candidates, filters passed, trades placed) and exposes `DumpAndResetDiagnostics()` which returns a diagnostic summary string and resets counters.
- `EnableDebugLogging` is a public static flag in the strategy that controls console debug output (set to `true` in the current code). Toggle it off to reduce console logging.

Order labeling and timestamps
- Orders placed by the strategy include a label like `"Hull 35/100"` (short/long lengths).
- For backtests, the `OrderManager` and UI use the kline close time to populate the recent trades timestamp (instead of DateTime.UtcNow).

Notes and suggestions
- Consider making `MinVolumeRatio`, `UseVolumeConfirmation`, and `UseMomentumConfirmation` configurable via `appsettings.json` instead of `const` for faster tuning.
- The volume method currently returns `false` if fewer than 21 klines are available; keep that in mind for low-data intervals.

---

## Pine Script v5 indicator (visualize HMAs, RSI, volume ratio, and signals)

Copy the following into TradingView (Pine Script v5) -> New Indicator. This reproduces the short/long HMA, the RSI "boring zone", the recent/avg volume ratio, and plots buy/sell markers where cross + filters would allow a trade.

```pinescript
//@version=5
indicator("Hull SMA Strategy (visual)", overlay=true)

// Parameters (match the C# defaults)
shortLen = input.int(35, "Short HMA Length")
longLen  = input.int(100, "Long HMA Length")
rsiLen   = input.int(20, "RSI Length")
rsiLow   = input.int(40, "RSI Lower Bound")
rsiHigh  = input.int(60, "RSI Upper Bound")
momLen   = input.int(5, "Momentum Period")
useVolume = input.bool(true, "Use Volume Confirmation")
minVolRatio = input.float(1.0, "Min Volume Ratio", step=0.1)

// HMA using built-in function
shortH = ta.hma(close, shortLen)
longH = ta.hma(close, longLen)

plot(shortH, color=color.orange, title="HMA Short")
plot(longH, color=color.blue, title="HMA Long")

// RSI
rsi = ta.rsi(close, rsiLen)

// Volume confirmation: recent = sma(volume,5), avg = sma(volume,20)
recentVol = ta.sma(volume, 5)
avgVol = ta.sma(volume, 20)
volRatio = recentVol / math.max(avgVol, 1)

// Signals
isRsiOk = (rsi < rsiLow) or (rsi > rsiHigh)
isVolOk = not useVolume or (volRatio >= minVolRatio)

crossUp = (shortH > longH) and (ta.change(shortH) > 0) and (ta.change(longH) >= 0) and (shortH[1] <= longH[1])
crossDown = (shortH < longH) and (ta.change(shortH) < 0) and (ta.change(longH) <= 0) and (shortH[1] >= longH[1])

// Momentum confirmation (percent over momLen)
momPercent = 100 * (close - close[momLen]) / math.max(close[momLen], 1)
momOkLong = momPercent > 0
momOkShort = momPercent < 0

longSignal = crossUp and isRsiOk and isVolOk and momOkLong
shortSignal = crossDown and isRsiOk and isVolOk and momOkShort

// Background highlight for signals (green for long, red for short)
// Adjust transparency by changing the second parameter (0 = opaque, 100 = fully transparent)
bgcolor(longSignal ? color.new(color.green, 85) : na, title="Long Background")
bgcolor(shortSignal ? color.new(color.red, 85) : na, title="Short Background")


// Tooltip notes
// - The indicator attempts to mirror the C# logic: HMA crossing + RSI outside 40-60 + recent volume vs 20-sample avg + short momentum.
// - Adjust the inputs to match your backtest parameters.

```

---

If you want, I can also:
- Add a CSV dump in the runner that writes per-symbol volume ratios and whether signals were blocked (helps pick `MinVolumeRatio`).
- Make `MinVolumeRatio` and other parameters read from `appsettings.json` so you can tune without recompiling.

If you'd like the Pine Script changed (for example, to use median volume or show the 4 EHMA component values), tell me what you prefer and I'll update it.

