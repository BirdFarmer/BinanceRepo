# FVG Strategy Summary

This is a concise summary of the updated FVG (Fair Value Gap) strategy implementation. The strategy now removes any dependency on order-book validation (so backtests match live/paper modes) and uses lightweight trend filters to improve signal quality.

## Class Overview (implementation)

This document describes the C# `FVGStrategy` implementation used in the codebase.

- **Responsibility:** detect Fair Value Gap (FVG) zones from closed candles, evaluate retest/clear conditions, apply optional trend filters, and request order placement when entry rules are met.
- **Inputs:** a sequence of closed `Kline` objects (historical or live closed candles). The strategy respects the runtime flag `StrategyRuntimeConfig.UseClosedCandles` for whether to ignore the forming candle.
- **Key methods:**
    - `IdentifyFVGs(List<Kline> closedKlines, string symbol)` — scans recent candles and returns detected FVG zones.
    - `RunAsync(string symbol, string interval)` — main live/paper execution loop: fetches recent klines, identifies and prunes FVGs, checks entry conditions and calls `OrderManager.PlaceLongOrderAsync` or `PlaceShortOrderAsync` when appropriate.
    - `RunOnHistoricalDataAsync(IEnumerable<Kline> klines)` — backtest/historical path that uses the same detection and entry logic but runs deterministically over historical data.
- **Entry decision flow:** detect FVGs, require retest of the zone by a prior candle and a clearing move by the current candle, optionally require trend alignment (e.g., EMA50/ADX as configured in this repository), then log an `[FVG ENTRY]` message and call the order manager.
- **Outputs / side-effects:** creates `Trade` placement requests via `OrderManager` (paper or live depending on operation mode), writes strategy-specific logs to the central logger/console, and updates internal FVG state (removing fulfilled zones).
- **Integration points:** interacts with `OrderManager` for placing entries, `Wallet` for paper trades, and `StrategyRuntimeConfig` for runtime flags. The strategy is executed by the `StrategyRunner` which may run multiple strategy instances concurrently.


## What's Changed
- Removed order-book fetching and imbalance checks — entries are now entirely price-action + indicator based so they are reproducible in historical backtests.
- Added trend alignment filters:
  - **EMA(50)**: only take longs when price > EMA50 and shorts when price < EMA50.
  - **ADX(14)**: require ADX >= configurable threshold (default 20) to ensure a meaningful trend.
- Volume logic: intentionally unchanged for now (no volume filters added).

## Key Entry Rules (summary)
- Detect FVG zones using the canonical 3-candle rule (compare first and third candle highs/lows).
- Require two most-recent FVGs of the same type (bullish or bearish) before considering an entry.
- Long entry: previous candle retests the bullish FVG (previous low inside the gap) and the current candle moves above the FVG upper bound; current price must be above EMA(50) and ADX >= threshold.
- Short entry: previous candle retests the bearish FVG (previous high inside the gap) and the current candle moves below the FVG lower bound; current price must be below EMA(50) and ADX >= threshold.

## Backtesting Notes
- Risk-reward baseline: strict 1:1 to evaluate raw signal quality.
- Timeframes to test: 5m, 15m, 1h, 4h.
- Markets: BTC, ETH, and major alts across multiple market regimes (trending, ranging, volatile).
- Primary objective: achieve >55% win rate at 1:1 RR; secondary targets: profit factor >1.5, max drawdown <15%.

## TradingView Pine Script (v5)
The script below replicates the strategy's entry visualization. It scans recent bars for FVG zones, requires two consecutive same-type FVGs, applies EMA(50) and ADX filters, and paints a green background on long entry candles and red background on short entry candles.

Paste this into TradingView's Pine editor (version 5). The script is background-only and intends only to visualize entries — it does not place orders.

```pinescript
//@version=5
indicator("FVG Entries (EMA50 + ADX)", overlay=true)

// User inputs
lookback = input.int(36, "FVG Lookback", minval=6)
emaLen = input.int(50, "EMA Length", minval=1)
adxLen = input.int(14, "ADX Length", minval=1)
adxThresh = input.float(20.0, "ADX Threshold", minval=0.0)

// Helper containers for zones
var float[] lower = array.new_float()
var float[] upper = array.new_float()
var int[] types = array.new_int() // 1 = bullish, -1 = bearish

array.clear(lower)
array.clear(upper)
array.clear(types)

// Scan recent bars to detect FVGs (3-bar rule: compare bar i-2 and i)
// We'll collect all FVGs found within the lookback window
for offset = 0 to lookback - 3
    firstHigh = high[offset + 2]
    firstLow = low[offset + 2]
    thirdHigh = high[offset]
    thirdLow = low[offset]

    if firstHigh < thirdLow
        array.push(lower, firstHigh)
        array.push(upper, thirdLow)
        array.push(types, 1)
    else if firstLow > thirdHigh
        // bearish gap: upper bound = firstLow, lower bound = thirdHigh
        array.push(lower, thirdHigh)
        array.push(upper, firstLow)
        array.push(types, -1)

// Find last two FVGs (if any)
fvgCount = array.size(types)
hasTwo = fvgCount >= 2
longEntry = false
shortEntry = false

ema = ta.ema(close, emaLen)
[dip, din, adx] = ta.dmi(adxLen, adxLen)

if hasTwo
    lastIdx = fvgCount - 1
    secondLastIdx = fvgCount - 2
    lastType = array.get(types, lastIdx)
    secondType = array.get(types, secondLastIdx)
    if lastType == secondType
        lastLower = array.get(lower, lastIdx)
        lastUpper = array.get(upper, lastIdx)

        // previous candle retest and current candle clearing the zone
        prevLow = low[1]
        prevHigh = high[1]
        curLow = low
        curHigh = high

        trendLong = close > ema
        trendShort = close < ema
        adxOk = adx >= adxThresh

        if lastType == 1
            // bullish: previous low inside gap and current low > upper bound
            if prevLow >= lastLower and prevLow <= lastUpper and curLow > lastUpper and trendLong and adxOk
                longEntry := true
        else
            // bearish: previous high inside gap and current high < lower bound
            if prevHigh <= lastUpper and prevHigh >= lastLower and curHigh < lastLower and trendShort and adxOk
                shortEntry := true

// Paint backgrounds on entry candles
bgcolor(longEntry ? color.new(color.green, 80) : na)
bgcolor(shortEntry ? color.new(color.red, 80) : na)

// Optional: plot EMA for visual aid
plot(ema, color=color.yellow, linewidth=2)
```

---

If you want I can:
- expose `EMA` and `ADX` parameters in a runtime config so they're editable without changing code,
- run a quick backtest across a symbol/timeframe and report signal counts (I can run locally and share results), or
- update the project docs to reflect these changes in `docs/strategies/detail/fvg-strategy.md`.

Which of those would you like next?

---
