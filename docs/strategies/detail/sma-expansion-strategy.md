# SMA Expansion Strategy

- Code: `BinanceTestnet/Strategies/SMAExpansionStrategy.cs`
- Purpose: Enter when momentum “expands” in one direction and the long-term trend is turning/confirming.

## Candle policy

- Honors the global “Use Closed Candles” checkbox in the desktop app.
- When enabled, signals are evaluated on the last fully closed candle (no forming candle); otherwise, the most recent forming candle is used.

## Indicators and settings

- Simple moving averages (SMAs): 25, 50, 100, 200 periods derived from quote history built by `Indicator.GetSma`.
- Strict expansion detector: `ExpandingAverages.CheckSMAExpansion(...)` for historical and `ConfirmThe200Turn(...)` for live checks. Conceptually it confirms “expansion + 200SMA turning.”
- Relaxed fallback (on when strict is neutral): stacked trend check
	- Long if SMA25 > SMA50 > SMA100 > SMA200 and SMA200 slope is up.
	- Short if SMA25 < SMA50 < SMA100 < SMA200 and SMA200 slope is down.
- Debounce window: `ExpansionWindowSize = 1` (requires N consecutive expansion results before acting—currently 1).

## Entry logic (succinct contract)

- Inputs: klines for the selected symbol/timeframe; UseClosedCandles flag; SMAs (25/50/100/200); optional fallback.
- Output: at most one entry per evaluation cycle (LONG or SHORT) if expansion resolves to +1 or −1.
- Error modes: insufficient kline history (no SMAs), detector returns neutral 0 (no entry), network errors when fetching current price (live path returns 0 and skips).

### Algorithm (live path)
1. Fetch klines and build quote history (respecting closed-candle policy for indicator inputs).
2. Compute SMA25/50/100/200.
3. Strict signal: `ConfirmThe200Turn(SMA25, SMA50, SMA100, SMA200, idx)` → result ∈ {−1, 0, +1}.
4. If result = 0 and relaxed mode is enabled, compute stacked fallback (see above) → result ∈ {−1, 0, +1}.
5. Track in `recentExpansions[symbol]` queue of size `ExpansionWindowSize`.
6. If all values in the window are +1 → LONG; if all −1 → SHORT.
7. Place order via `OrderManager.PlaceLong/ShortOrderAsync` using the selected signal candle’s timestamp and the current market price.
8. Call `OrderManager.CheckAndCloseTrades(...)` to maintain exits.

### Algorithm (historical path)
1. Pre-compute SMA25/50/100/200 over the full historical series once.
2. For each index where SMA200 is available, evaluate strict expansion at that point; if neutral and relaxed mode enabled, evaluate stacked fallback.
3. Emit LONG/SHORT using the historical candle’s close and timestamp.
4. After each signal, call `CheckAndCloseTrades` to simulate exits.

## Exits and risk

- This strategy delegates stop-loss / take-profit / trailing behavior to the shared `OrderManager` per your app-wide settings (e.g., ATR TP or Trailing Stop via the UI).
- Position sizing, leverage, and other risk parameters are configured in the main app UI.

## Tuning knobs

- SMA lengths (currently 25/50/100/200): lowering lengths produces more, earlier signals; increasing lengths produces fewer, slower signals.
- Relaxed fallback: enabled in code to ensure signals appear when strict expansion is neutral. This can be parameterized if you want an explicit UI toggle.
- Expansion window (`ExpansionWindowSize`): require more than one consecutive expansion result to filter noise (e.g., 2 or 3).

## Practical guidance

- Timeframes: start with 5m or 15m to reduce micro noise; use 1m only for fast smoke tests.
- Symbols: prefer liquid, volatile pairs for more consistent expansion signals.
- Smoke test: run a short backtest window first; if the strict detector is too selective, rely on the relaxed fallback (already enabled) to validate wiring end-to-end.

## Debugging and logs

- The strategy prints concise console lines when tracking expansion and when placing orders.
- For deeper auditing, you can add `TraceSignalCandle(...)` calls (as used in other strategies) at the decision point to capture:
	- Mode (CLOSED/FORMING), signal candle time, previous candle time, price, and whether strict vs relaxed logic fired.

## Known limitations

- Strict expansion detector can remain neutral for long stretches in ranging markets. The relaxed fallback is a practical compromise for testing and for momentum-lite environments.
- Stacked fallback can still produce whipsaws during choppy transitions; increasing `ExpansionWindowSize` or adding a minimum separation threshold between SMAs can help filter noise.

## Future improvements

- Parameterize relaxed fallback and window size via the desktop UI.
- Add optional minimum SMA separation (e.g., 0.1%–0.3%) to confirm “expansion,” not just ordering.
- Optional volume or ATR filters to prefer higher-quality expansions.
- Add `TraceSignalCandle` instrumentation by default for uniform signal logging across strategies.
