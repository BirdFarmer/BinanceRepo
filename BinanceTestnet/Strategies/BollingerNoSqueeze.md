**Bollinger No-Squeeze Breakout (Trend-Confirmed)**

- **File:** `BinanceTestnet/Strategies/BollingerNoSqueeze.md`
- **Purpose:** Describe the "no-squeeze" Bollinger breakout strategy implemented in `BollingerSqueezeStrategy.cs` (new/experimental replacement). Focuses on entering breakouts when Bollinger Bands are wide (no squeeze) and a trend filter confirms direction.

**Overview**
- **Idea:** Instead of trading squeezes, trade breakouts that occur when the market is already volatile (BB width is large). Require trend confirmation so we follow the prevailing move rather than fading it.
- **Trade Type:** Breakout / trend-following (long-focused by default; symmetric short rules possible).

**Indicators & Settings**
- **Bollinger Bands:** `length` = 20, `stdDev` = 2.0 (default)
- **ATR:** `period` = 14 (used to normalize BB width and for TP sizing guidance)
- **EMA (trend):** `EMAtrend` = 50 (filter price > EMA for longs)
- **ADX (optional):** `ADXperiod` = 14, **threshold** = 20 (optional trend strength filter)
- **Volume filter (optional):** `volumeMultiplier` = 1.2 (require volume > avgVolume * multiplier)
- **No-squeeze threshold:** `SqueezeMin` (bbWidthNormalized >= SqueezeMin). Default `SqueezeMin = 1.2` where bbWidthNormalized = (upper - lower) / ATR
- **Take Profit guidance:** `TPmult = 2.0 * ATR` (suggested), but stop-losses are handled by the application-level risk manager and should not be implemented inside this strategy class.
- **Warmup:** Ensure at least max(BB length, ATR period, EMA length) candles before evaluating signals

**Entry Logic (Long)**
- Evaluate on **closed candle** (use closed candles to avoid intra-bar ambiguity).
- Compute `bbWidth = upper - lower` and `bbWidthNorm = bbWidth / ATR` (use current ATR value).
- Conditions (all required unless noted optional):
  - **No-squeeze:** `bbWidthNorm >= SqueezeMin`  (ensures BB are wide enough)
  - **Confirmed Close Above Band:** `Close > UpperBand` (confirmed close above upper, not just wick)
  - **Trend Filter:** `Close > EMAtrend` (EMA50) — this reduces fading trending breakouts
  - **Optional Strength Filters:** `ADX > ADXthreshold` and/or `Volume > avgVolume * volumeMultiplier`
- When all conditions are met, **enter long** at the next bar open (or immediate market depending on execution policy).

**Entry Logic (Short)**
- Mirror long logic:
  - `bbWidthNorm >= SqueezeMin`
  - `Close < LowerBand`
  - `Close < EMAtrend` (EMA50)
  - Optional: `ADX > ADXthreshold`, `Volume > avgVolume * volumeMultiplier`
  
**Backtest / Testing Notes**
- Use closed candles in both historical and live modes for parity.
- Model slippage and commission for realistic returns.
- Warmup: skip first `warmup = max(BB length, ATR period, EMA length)` bars.
- Collect metrics: trade count, win rate, avg RR, average hold time, max drawdown.
- If trade frequency is too low, try:
  - Lowering `SqueezeMin` (e.g., 1.0),
  - Relaxing the volume filter,
  - Using `Close >= UpperBand` instead of strict `>`.

**Debug Logging Suggestions**
- Log per-symbol, per-signal lines that include:
  - `Time, Close, UpperBand, LowerBand, MiddleBand, ATR, bbWidthNorm, EMAtrend, ADX, Volume, avgVolume`
- Log why a signal failed: which of the entry conditions was false (no-squeeze, not above upper, trend fail, volume fail).
- When backtesting many symbols, aggregate counts of fails by reason to quickly identify bottlenecks.

**Variants & Tuning**
- **Long-term variant:** Use `BB length = 50` or `200` for slower regimes (works better on 1h+ charts).
- **Aggressive variant:** Increase `TPmult` to 3.0 for larger reward targets (manage stops at app-level accordingly).
- **Volume-less variant:** Skip volume filter for low-liquidity altcoins and rely on EMA + ADX.

**Pseudocode**
```
if (bars < warmup) skip
bbWidth = upper - lower
bbWidthNorm = bbWidth / ATR
if (bbWidthNorm >= SqueezeMin && Close > upperBand && Close > EMA50 && (ADX > ADXthresh) && (Volume > avgVol*volMult))
  enter_long(next_open)
  provide_target = entry + TPmult * ATR  // strategy suggests TP; app manages SL/TP placement
```

**Notes for Implementation in this Repo**
- Implement in `BollingerSqueezeStrategy.cs` (temporary name) as requested.
- Keep `BollingerSqueezeSettings.DebugMode` available and expand debug output to include reasons for failed conditions.
- Provide configuration fields for all key knobs (BB length, stdDev, ATR period, EMA length, SqueezeMin, TPmult, volumeMultiplier, ADX settings). Note: do not implement SL placement in this strategy; the app handles stop-losses.

---

If you want, I can now:
- Patch the `BollingerSqueezeStrategy.cs` file to replace the current logic with this rule-set and add the new settings and debug logs, then run `dotnet build` to check compilation.
- Or create a separate strategy file and wire it into backtests; say which you prefer.

Which action do you want next?