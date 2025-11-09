```markdown
# MACDStandardStrategy

- Code: `BinanceTestnet/Strategies/MACDStandardStrategy.cs`
- Goal: Classic MACD momentum rotation with optional trend filter.

## Indicators
- MACD (12,26,9 typical)
- Optional SMA/EMA context

## Long entries — where and why
- MACD line crosses above signal; histogram flips positive
- Optional: price above trend filter
- Rationale: first leg of bullish rotation.

## Short entries — where and why
- MACD line crosses below signal; histogram negative
- Optional: price below trend filter
- Rationale: momentum turning down.

## Notes
- Favor closed candles to avoid signal flicker.

## Usage recommendations (practical)

- Market regime: works best in trending conditions; avoid tight ranges/chop. The EMA(50) filter helps, but ranging periods can still produce noise.
- Timeframes: lower intraday (e.g., 5m–15m) for more signals; 1h+ for fewer but often cleaner rotations. Match to your execution appetite.
- Expectations: frequent, smaller moves when trend persists; patience required through consolidation.
- Risk: prefer tight, structure-based stops (e.g., beyond prior swing/structure or a small ATR multiple). Keep losses small; let winners reach TP.
- Filters: keep the trend EMA enabled for production; disable only for experimentation.
- Validation: use the provided background-only Pine indicator with closed-candle mode to visually confirm signals before deploying.

### Suggested defaults (initial)

| Parameter            | Suggested | Notes |
|----------------------|----------:|-------|
| Timeframe            | 5m        | High signal frequency; adjust to 15m if too noisy. |
| Trend EMA Length     | 50        | Current tuning; consider 34–55 range later. |
| Take Profit Multiple | 1.5R      | Balanced vs typical MACD momentum follow-through. |
| Stop Loss Multiple   | 1.0R      | Tight to cut failed rotations early. |
| Position Sizing      | Small / static | Keep sizing conservative until expectancy confirmed. |
| Closed-Candle Mode   | Enabled   | Prevents flicker / premature signals. |

Rationale: A 1.5R TP with 1.0R SL maintains a payoff >1 if win rate holds near or above break-even after filtering. Adjust after combo harness validation.

## Tuning

- Scope: Minimal, testable tuning to reduce whipsaw while preserving closed-candle behaviour.
- Change: Add an EMA(50) trend filter that requires price to be above EMA for long entries and below EMA for short entries.
- Rationale: MACD cross signals are reliable in trending environments but generate false entries during range-bound periods; the EMA(50) filter reduces counter-trend signals with a very small footprint.
- Safety: Preserves closed-candle policy (no repaint). Implemented as a minimal filter in code to keep behavior simple and testable.

### Validation (lightweight)

Visual confirmation only for this iteration:
1. Background paints green/red strictly on confirmed MACD signal crosses in trend direction.
2. Fewer signals during obvious range chop (EMA acts as filter).
3. Closed-candle toggle in PineScript prevents intra-bar flicker.

> CandleDistribution intentionally excluded per global constraints.

## PineScript indicator (background-only)

The snippet below is a TradingView Pine Script v5 indicator. It is background-only (no labels/triangles/arrows), respects the closed-candle toggle (uses barstate.isconfirmed when closed-candle mode is enabled), and implements the EMA(50) trend filter described above.

```pinescript
//@version=5
indicator("MACDStandard — Background Only", overlay=true)

// Inputs
use_closed = input.bool(true, "Use closed candles (no repaint)")
fastLen = input.int(12, "MACD Fast Length")
slowLen = input.int(26, "MACD Slow Length")
signalLen = input.int(9, "MACD Signal Length")
emaLen = input.int(50, "Trend EMA Length")
bgOpacity = input.int(85, "Background opacity (0-100)")
// Optional EMA visual (off by default to keep background-only contract)
show_trend_ema = input.bool(false, "Show Trend EMA line")

// Compute MACD (uses close series)
[macdLine, signalLine, _] = ta.macd(close, fastLen, slowLen, signalLen)

// Trend filter
trendEma = ta.ema(close, emaLen)
trendLong = close > trendEma
trendShort = close < trendEma

// Cross detection
rawLong = ta.crossover(macdLine, signalLine)
rawShort = ta.crossunder(macdLine, signalLine)

// Respect closed-candle toggle to avoid repaint: only accept signals once bar is confirmed
finalLong = use_closed ? (rawLong and barstate.isconfirmed and trendLong) : (rawLong and trendLong)
finalShort = use_closed ? (rawShort and barstate.isconfirmed and trendShort) : (rawShort and trendShort)

// Background highlights only
bgcolor(finalLong ? color.new(color.green, bgOpacity) : na)
bgcolor(finalShort ? color.new(color.red, bgOpacity) : na)

// No labels or arrows; optional EMA line only (disabled by default)
plot(show_trend_ema ? trendEma : na, title="Trend EMA", color=color.new(color.yellow, 0), linewidth=2)
```

Copy this snippet into a new TradingView indicator. Toggle "Use closed candles" on to avoid intra-bar repainting and match the repo's closed-candle policy.

Note: This script intentionally does not plot MACD/Signal lines on the price pane. For traditional MACD visualization, add TradingView’s built-in MACD as a separate lower panel, while keeping this background-only indicator on the chart. The Trend EMA line overlays naturally on price and can be enabled for clarity.

```

## Findings & recommendations (from 3 backtests)

Summary of your 3 runs on 15m with code as-is (EMA50 trend filter; closed-candle policy):

- Run 1 — Trailing ON, symbols: ETH, SOL, XRP, ZEC, ALPACA
	- Trades: 83 (41W / 42L) | Win Rate: 49.4%
	- Net PnL: +50.74 | Expectancy ≈ +0.61 per trade
	- Long avg +0.66 | Short avg +0.57 | Avg duration ≈ 107m
	- Note: Lower win rate but strong tail wins (best ~+7–8), consistent with trailing.

- Run 2 — Fixed exits (no trailing), same symbols
	- Trades: 84 (46W / 38L) | Win Rate: 54.8%
	- Net PnL: +10.25 | Expectancy ≈ +0.12 per trade
	- Long avg +0.20 | Short avg +0.05 | Avg duration ≈ 64m
	- Note: Higher win rate but much lower payoff; tails got cut.

- Run 3 — Trailing ON, momentum basket: DOGE, AVAX, LINK, INJ, NEAR
	- Trades: 114 (62W / 52L) | Win Rate: 54.4%
	- Net PnL: +78.27 | Expectancy ≈ +0.69 per trade
	- Long avg +0.96 | Short avg +0.52 | Avg duration ≈ 113m
	- Note: Best overall result. Trailing + momentum coins amplified trend captures (best long ~+15.9).

What this suggests

- Trailing captures the strategy’s edge: Expectancy and Sharpe benefit from letting winners run, even if win rate doesn’t jump.
- Symbol selection matters more than exit style alone: a momentum-focused basket (DOGE/AVAX/LINK/INJ/NEAR) outperformed the mixed set.
- Shorts tended to perform at least as well as longs in the first two runs, hinting at a mildly bearish or range regime; trailing helped both sides in Run 3.
- Trailing increases average trade duration (≈ +45–50 minutes vs fixed), which is expected and acceptable when expectancy improves.

Actionable recommendations (entry-centric, no code changes required)

1) Default to a momentum basket on 15m when the market isn’t flat
	 - Primary: DOGE, AVAX, LINK, INJ, NEAR
	 - Secondary adds (when their ATR/Price > ~0.7%): SOL, BNB
	 - Rationale: These names exhibited stronger continuation and larger tail winners under trailing.

2) Use trailing in trend mode; use fixed exits only to cap churn in obvious chop
	 - Trailing ON (trend conditions): activation ~0.6–0.9× ATR% (start ~0.8×), callback ~0.3–0.5× ATR% (start ~0.35×)
	 - Fixed exits (chop): keep ATR-based TP/SL; expect higher win rate but lower payoff (Run 2 profile)

3) Keep shorts enabled
	 - Shorts consistently held their own or led in win rate; don’t restrict to longs-only unless regime analysis is strongly bullish.

4) Timeframe guidance
	 - 15m is a solid default for signal density and trend capture via trailing.
	 - If signal quality degrades (tight ranges), consider testing 1h on majors (BTC, ETH, SOL, BNB) for fewer but higher-quality entries.

Suggested presets

- Trend preset (recommended default on 15m)
	- Basket: DOGE, AVAX, LINK, INJ, NEAR (+SOL/BNB when volatile)
	- Entries: MACD cross + EMA50 context (as-is)
	- Exits: Trailing ON; activation ~0.8× ATR%, callback ~0.35× ATR%
	- Concurrency cap: 4–6 symbols (to reduce correlated drawdowns)

- Chop preset (defensive)
	- Basket: Liquidity leaders (BTC, ETH, SOL, BNB)
	- Entries: MACD cross + EMA50 context (as-is)
	- Exits: Fixed ATR TP/SL (≈1.2R TP, 1.0R SL equivalent)
	- Concurrency cap: 3–4

Next low-risk validations (optional)

- Compare the above presets over the last 2–3 distinct weeks (quiet vs volatile) to ensure the delta holds across regimes.
- If momentum basket underperforms, temporarily prune the lowest contributor(s) by expectancy (e.g., lowest Avg PnL per trade over the last 7 days).

Potential future entry refinements to test (when you want to iterate)

- MACD zero-line filter: require MACD > 0 for longs, < 0 for shorts to avoid counter-trend flips.
- MACD histogram acceleration: for longs, hist[i] > hist[i-1] and > 0 (mirror for shorts) to confirm growing momentum.
- Volatility floor: skip entries when ATR(14)/Close < ~0.5% on 15m; low-vol crosses are noisier.
- HTF agreement (1h): prefer entries when 1h EMA50 slope agrees with 15m signal.

These are purely entry-side and can be evaluated without changing current exits; adopt only if expectancy rises while trade count remains acceptable.
