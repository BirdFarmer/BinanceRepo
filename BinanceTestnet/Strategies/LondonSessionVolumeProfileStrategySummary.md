**London Session Volume Profile (FRVP) — Strategy Summary**

This document explains the `LondonSessionVolumeProfileStrategy` in plain language for traders (not developers). It describes what the strategy watches for, the key settings you can change, how to use it in practice, and a simple Pine Script you can paste into TradingView to visualize the behaviour.

**What it does:**
- Computes a Fixed-Range Volume Profile (FRVP) for the configured London session and finds the Point-of-Control (POC), Value Area High (VAH) and Value Area Low (VAL).
- Detects a post-session breakout when the first post-session candle closes beyond the session high/low (configurable field: `Close`, `High`, or `Low`).
- On breakout, the strategy does NOT enter immediately. Instead it creates a "watcher" (LookingForLong/Short) with a target entry price (midpoint between session extreme and VAH/VAL) and an expiry.
- The strategy places a MARKET entry only when a later post-session candle (a subsequent candle strictly after the breakout signal) actually touches the target price (the candle's high/low range includes the target). This captures wick touches.
- Stop-loss (SL) is set at the session POC. Take-profit (TP) logic is intended to be 2:1 reward-to-risk (RR) but exact TP placement depends on order execution code — check your `OrderManager` if you need modifications.

**Why this approach?**
- The watcher pattern avoids entering on the same candle that produced the breakout (reduces false early entries).
- Using FRVP gives a price-level view built from the session's volume distribution, which often marks meaningful support/resistance (POC/VAH/VAL).
- Market-on-touch after the breakout reduces missed opportunities while ensuring the break was meaningful enough to retest the target.

**Key user-facing settings (where to change them)**
Edit `user.settings.json` used by the desktop app (or modify `BinanceTestnet/Config/UserSettingsReader.cs` defaults if experimenting):

- `LondonSessionStart` and `LondonSessionEnd` — session window (HH:mm). Default: `08:00` to `14:30` UTC.
- `LondonBreakCheck` — which field triggers a breakout: `Close`, `High`, or `Low`. Default: `Close`.
- `LondonValueAreaPercent` — percent for the value area (e.g., `70` = 70%). Default: `70`.
- `LondonBuckets` — buckets used to compute FRVP. Default: `120`.
- `LondonScanDurationHours` — how long a watcher stays active (hours). Default: `4.0`.
- `LondonUseOrderBookVap` — prefer order-book VAP when available; FRVP is used as a fallback. Default: `true`.
- `LondonAllowBothSides` — if false, trading stops after any side is placed for the session. Default: `false`.
- `LondonMaxEntriesPerSidePerSession` — maximum number of MARKET entries allowed per side (LONG/SHORT) per session. Default: `1` (set to `2` to allow up to two longs, etc.).
- `LondonPocSanityPercent` — percent of session range used to sanity-check POC. Default: `2.0`.
- `LondonEnableDebug` — verbose logs for debugging the strategy. Default: `false`.

- UI note: In the TradingAppDesktop Settings window this timeout is shown as **`Watcher expiry (min)`** (was previously labeled "Limit Expiry"). It controls how many minutes a watcher remains active after the breakout signal (0 = no expiry).

Paths and files you may edit:
- Strategy code: `BinanceTestnet/Strategies/LondonSessionVolumeProfileStrategy.cs`
- Settings DTO and loader defaults: `BinanceTestnet/Config/UserSettingsReader.cs`
- Desktop settings used at runtime: look for `user.settings.json` in the app directory (TradingAppDesktop).

**How to run / test quickly**
- Start the desktop app (or your paper runner) with `London` strategy selected.
- If you want to speed testing without waiting for the real London/New York times, change the `LondonSessionEnd` to a time about 10 minutes ago relative to now — this makes the strategy treat the recent candles as "post-session" and triggers FRVP calculation immediately.
- Turn on `LondonEnableDebug=true` to get verbose logs describing session window, FRVP values, watcher creation, and per-cycle watcher diagnostics.

**How to interpret logs**
- Example logs you will see when debug is enabled:
  - `Session window for SYMBOL: ... Collected N candles.`
  - `Using FRVP for SYMBOL: POC=... VAH=... VAL=...` — session profile computed.
  - `LONG breakout detected for SYMBOL. Watching for touch at X until YYYY` — watcher created.
  - `WatcherState for SYMBOL: target=..., isLong=..., signal=..., expiry=...` — per-cycle watcher state.
  - `Current candle RANGE-INCLUDE LONG trigger for SYMBOL ... Entering market.` — watcher fired and market entry executed.

**Quick tip for faster local testing**
- Set `LondonSessionEnd` to `now - 10 minutes` (or a few minutes earlier). This forces the strategy to treat recent candles as post-session so it computes FRVP immediately and you can test breakouts without waiting for the actual London/New York session boundaries.

**Behavior details & constraints**
- Watchers are preserved across strategy instance cycles (they're stored in a thread-safe shared store), which prevents missed triggers caused by short-lived strategy instances.
- Trigger semantics: the watcher's firing candle must be strictly later than the signal candle (no same-candle entries) and the candle range must include the watcher's target price.
- SL is set to the session POC (Point-of-Control). TP is intended to be 2:1 RR.
- If `LondonAllowBothSides=false`, a single entry (any side) will stop further trading for that session. If you want multiple entries, increase `LondonMaxEntriesPerSidePerSession`.
- Clarification: `LondonAllowBothSides` and `LondonMaxEntriesPerSidePerSession` are applied per symbol. That means if `LondonAllowBothSides=false` and `BTCUSDT` has a long entry, the strategy will not create a short watcher for `BTCUSDT` for the rest of the session, but other symbols (e.g. `ETHUSDT`) can still create watchers and trade independently. `LondonMaxEntriesPerSidePerSession` limits entries per side per symbol (default=1).
- The strategy uses FRVP (OHLCV distribution) as a reliable fallback; order-book VAP is preferred when available but may be skipped if not usable.

**Recommended Timeframes & Performance**

- **Recommended TF:** This strategy is designed for fast reaction to post-session breakouts and is best used on the **1-minute** or **5-minute** chart. These timeframes balance responsiveness and noise for short-lived post-session moves.
- **Performance note for many symbols:** If you run the strategy across **more than ~50 symbols**, a `1m` timeframe may cause the scanning cycle to be overrun (the next candle arrives before the strategy finishes processing all symbols). To mitigate:
  - Turn off verbose debug logging (`LondonEnableDebug=false`) to reduce console I/O and processing overhead. You can toggle debug on/off in `user.settings.json` mid-session.
  - Use the `5m` timeframe instead of `1m` when scanning many symbols.
  - Reduce the number of symbols being scanned concurrently.

These simple steps usually eliminate cycle overruns without changing trading logic.

**Pine Script (TradingView) — simple visualizer**
Paste this script into TradingView Pine Editor to plot the session high/low and FRVP-derived POC/VAH/VAL as horizontal lines. This is a light visualization to help you compare the strategy's levels with chart action.

```pinescript
//@version=5
indicator("London Session FRVP (visual)", overlay=true)
// User inputs
sessionStart = input.time(defval=timestamp("2025-01-01T08:00:00Z"), title="Session Start (UTC)")
sessionEnd = input.time(defval=timestamp("2025-01-01T14:30:00Z"), title="Session End (UTC)")
vaPct = input.float(70, title="Value Area %", minval=1, maxval=100)

// Find today's session using timestamps (approx) — this is illustrative only
inSession = (time >= timestamp(year, month, dayofmonth, hour(sessionStart), minute(sessionStart))) and (time <= timestamp(year, month, dayofmonth, hour(sessionEnd), minute(sessionEnd)))

var float sessionHigh = na
var float sessionLow = na
var float sessionPOC = na
var float sessionVAH = na
var float sessionVAL = na

if inSession
    sessionHigh := math.max(nz(sessionHigh), high)
    sessionLow := na(sessionLow) ? low : math.min(sessionLow, low)
else
    // On first bar after session, compute simple proxies (this is not a true FRVP)
    if not na(sessionHigh) and not na(sessionLow)
        // crude POC proxy: median price weighted by volume (approx)
        sessionPOC := (sessionHigh + sessionLow) / 2
        sessionVAH := sessionHigh
        sessionVAL := sessionLow
        // Reset for next session
        sessionHigh := na
        sessionLow := na

// Plot lines
plot(sessionPOC, title="POC", color=color.orange, linewidth=2)
plot(sessionVAH, title="VAH", color=color.green)
plot(sessionVAL, title="VAL", color=color.red)
```

Note: The Pine script above is intentionally simple. It does not compute a true bucketed FRVP — implementing a true FRVP in Pine is possible but more complex. The script gives you a visual approximation to aid manual comparison.

**Example usage workflow**
1. Enable `LondonEnableDebug` in your `user.settings.json` and restart the desktop app.
2. Temporarily set `LondonSessionEnd` to `now - 10 minutes` to force immediate FRVP calculation for testing.
3. Watch console logs. When you see `LONG/SHORT breakout detected` and `Watching for touch at ...`, watch the subsequent candles. If a candle's wick touches the target and logs `RANGE-INCLUDE ... trigger`, a market order will be placed.
4. After testing, restore `LondonSessionEnd` to real session times and turn off debug logging.

**If you want changes**
- Want per-symbol max entries instead of global per-session limits? I can change counters to be per-symbol.
- Want the watcher to re-scan remaining snapshot candles immediately after creation (prevents missing a target that already exists in the snapshot)? I can add that small change.
- Want the setting exposed in the TradingAppDesktop settings UI? I can add the checkbox/field.

File location:
- `BinanceTestnet/Strategies/LondonSessionVolumeProfileStrategySummary.md`

If you want, I can also commit the new summary and add the UI setting next. Let me know which next step you prefer.
