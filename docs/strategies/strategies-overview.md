# Strategy Suite Overview

This document provides a concise functional overview of each strategy currently in the `BinanceTestnet.Strategies` namespace: what market condition it targets, the indicators/signals it uses, and the precise entry rationale ("where and why it enters a trade"). All strategies assume usage on the last fully closed candle unless otherwise noted. Future refactors will enforce closed-candle consistency across the board.

---
## AroonStrategy
**Purpose:** Capture emerging trend acceleration combining Aroon momentum, long-term SMA alignment, and an EHMA (optimized Hull moving average) crossover.
**Indicators:** Aroon(20), SMA(200), EHMA(70).
**Long Entry:** On a fresh EHMA bullish cross (EHMA > previous EHMA, previous EHMA was non-bullish) while price is below but turning up toward the rising SMA200 (price high < SMA and SMA ascending). Rationale: Enter early on momentum shift with room to revert to SMA mean.
**Short Entry:** Mirror logic: EHMA bearish cross while price above but turning down toward a falling SMA200. Rationale: Catch downside acceleration with mean reversion potential.
**Notes:** Aroon signal selection currently simplified; future improvement will refine up/down cross differentiation and closed-candle enforcement.

## HullSMAStrategy
**Purpose:** Trend continuation/reversal detection using dual EHMA lengths for faster + slower structure plus a simple RSI filter (if implemented in extended variants).
**Indicators:** EHMA(short=20), EHMA(long=100), optional RSI.
**Long Entry:** Short EHMA crosses above long EHMA and prior long EHMA direction confirms potential bullish transition. Often filtered by price action context (e.g., RSI oversold relief if added).
**Short Entry:** Short EHMA crosses below long EHMA with confirmation of bearish transition.
**Rationale:** Dual hull/ehma reduces lag vs. standard EMA crossover.

## EnhancedMACDStrategy
**Purpose:** Momentum confirmation merging MACD shifts, EMA trend bias, RSI sanity checks.
**Indicators:** MACD (fast/slow defaults), EMA(s) for directional bias, RSI for overbought/oversold rejection.
**Long Entry:** Bullish MACD histogram uptick or line cross while fast EMA > slow EMA and RSI avoids severe overbought extension; seeks sustained bullish expansion.
**Short Entry:** Bearish MACD cross while fast EMA < slow EMA and RSI not deeply oversold (prevents exhaustion shorts).
**Rationale:** Layer multiple momentum filters to reduce false early flips.

## FVGStrategy
**Purpose:** Exploit Fair Value Gaps (displacement + imbalance), seeking retest entries with order book volume confirmation.
**Concepts:** Fair Value Gap: Candle 1 high < Candle 3 low (bullish) OR Candle 1 low > Candle 3 high (bearish) leaves an untraded void.
**Entry Long:** After identifying at least two consecutive bullish gaps of same type, price moves back through upper boundary of nearest bullish gap and order book shows significant bid imbalance.
**Entry Short:** Same pattern for bearish gaps (price drops through lower boundary) with ask-side dominance.
**Rationale:** Institutional-style imbalance zones often act as continuation launchpads after shallow retests.

## SupportResistanceStrategy
**Purpose:** Structured breakout + retest system around algorithmically detected pivot-based support/resistance with volume + trend (ADX) validation.
**Indicators/Derived Data:** Pivot highs/lows (lookback window), Volume SMA(20), ADX(14), Engulfing pattern heuristic, breakout state trackers.
**Breakout Long Setup:** Closed candle above a recent resistance pivot with high relative volume (current volume > SMA*multiplier) and ADX >= threshold marks breakout active.
**Retest Long Entry:** Subsequent closed candle wicks down to broken resistance level with prior low staying above level and low-volume retest characteristics.
**Short Side:** Symmetric logic for support breakdown and bearish retest.
**Rationale:** Distinguish impulsive breakout from drift; enter on controlled liquidity retest minimizing chase risk.

## MACDStandardStrategy
**Purpose:** Classic MACD momentum shifts without extra hull/ehma complexity.
**Indicators:** MACD (default periods), Signal line, optionally SMA trend context.
**Long Entry:** MACD line crosses above signal and histogram turns positive while price confirmed by trend filter.
**Short Entry:** MACD line crosses below signal; histogram negative with downtrend confirmation.
**Rationale:** Capture first leg of momentum cycle.

## MACDDiversionStrategy
**Purpose:** Identify momentum divergence between price swing and MACD histogram/lines.
**Indicators:** MACD, swing high/low detection.
**Long Entry:** Bullish divergence (price makes lower low, MACD momentum makes higher low) followed by MACD bullish cross.
**Short Entry:** Bearish divergence (price higher high, MACD lower high) followed by bearish cross.
**Rationale:** Trade early reversal signals preceding full trend shift.

## RsiDivergenceStrategy
**Purpose:** Similar divergence approach using RSI instead of MACD for earlier inflection detection.
**Indicators:** RSI(14 or configured), swing point algorithm.
**Long Entry:** Bullish RSI divergence plus RSI exiting oversold zone.
**Short Entry:** Bearish RSI divergence plus RSI exiting overbought zone.
**Rationale:** RSI divergences often precede price structure pivots.

## RSIMomentumStrategy
**Purpose:** Straightforward momentum continuation using RSI midline bias.
**Indicators:** RSI.
**Long Entry:** RSI crossing upward through 50–55 band with prior higher low structure.
**Short Entry:** RSI crossing downward through 45–50 band with prior lower high structure.
**Rationale:** Ride directional expansion while avoiding extreme edges.

## SMAExpansionStrategy
**Purpose:** Volatility expansion around key SMA baseline.
**Indicators:** SMA fast vs. SMA long, possible range or ATR (if present in extended implementation).
**Long Entry:** Fast SMA accelerates away from long SMA with price pulling back to shallow support but rejecting a deeper mean reversion.
**Short Entry:** Mirror scenario for downside.
**Rationale:** Catch expansion phase early before overextension.

## SimpleSMA375Strategy
**Purpose:** Ultra-long horizon trend filter with SMA(375) for macro bias, entering on shorter-term confirmation.
**Indicators:** SMA(375) plus perhaps a shorter SMA or price location.
**Long Entry:** Price closes above SMA(375) after sustained time below, showing regime change.
**Short Entry:** Price closes below SMA(375) after time above.
**Rationale:** Distill major regime transitions; few signals, higher conviction.

## BollingerSqueezeStrategy
**Purpose:** Volatility contraction followed by expansion breakout.
**Indicators:** Bollinger Bands, Bandwidth/Squeeze metric, momentum filter (e.g., RSI or MACD) optional.
**Long Entry:** Price breaks upper band after sustained squeeze and momentum bias positive.
**Short Entry:** Break below lower band after squeeze with negative momentum.
**Rationale:** Trade volatility release following compression phase.

## EmaStochRsiStrategy
**Purpose:** Combine EMA trend bias with stochastic RSI timing.
**Indicators:** EMA fast/slow, StochRSI (%K/%D), possibly ATR filter.
**Long Entry:** Fast EMA > slow EMA, StochRSI %K crosses above %D from oversold region.
**Short Entry:** Fast EMA < slow EMA, %K crosses below %D from overbought.
**Rationale:** Align mean-reversion oscillations within prevailing trend direction.

## FibonacciRetracementStrategy
**Purpose:** Enter trend continuation at Fibonacci retracement zones after impulsive leg.
**Indicators/Data:** Recent swing high/low mapping, Fibonacci levels (38.2%, 50%, 61.8%).
**Long Entry:** Uptrend pullback holds 38.2–61.8 zone and shows bullish rejection (e.g., reversal pattern or momentum tick up).
**Short Entry:** Downtrend retracement into Fibonacci band with bearish rejection.
**Rationale:** Structured probabilistic re-entry at institutionally watched levels.

## IchimokuCloudStrategy
**Purpose:** Multifactor trend and momentum assessment using Ichimoku components.
**Indicators:** Tenkan-sen, Kijun-sen, Senkou Span A/B (Kumo cloud), Chikou span.
**Long Entry:** Price breaks above cloud, Tenkan above Kijun, Chikou above price, future cloud bullish.
**Short Entry:** Bearish mirror: price below cloud, Tenkan below Kijun, Chikou below price, future cloud bearish.
**Rationale:** Require broad consensus across components to reduce false breaks.

## CandleDistributionReversalStrategy
**Purpose:** Detect distribution/absorption sequences (e.g., multiple narrow-bodied candles followed by strong reversal candle) signaling imminent reversal.
**Indicators/Data:** Candle body/upper-lower wick ratios, cluster statistics, potential volume confirmation.
**Long Entry:** Down sequence (series of small range selling candles) followed by strong bullish engulfing closing near highs.
**Short Entry:** Up sequence followed by strong bearish reversal closing near lows.
**Rationale:** Identify exhaustion and supply/demand shift before broader indicators confirm.

---
## Common Entry Principles
- Closed Candle Bias: Signals should be generated from the last fully closed candle to avoid repainting (audit pending for remaining strategies).
- Volume / Imbalance Filters: Some strategies (SupportResistance, FVG) use volume or order book imbalance to validate structural setups.
- Trend + Trigger Separation: Many strategies separate a higher timeframe bias (SMA/EMA/cloud) from lower timeframe trigger (cross/divergence/pattern) to reduce noise.
- Divergence Use: Divergence-based strategies require confirmation (e.g., a cross or breakout) rather than acting on divergence alone.

## Planned Enhancements
1. Enforce closed-candle usage uniformly.
2. Add configuration abstraction for periods/thresholds.
3. Unified logging schema (strategy, symbol, interval, trigger type, price, timestamp).
4. Backtest mode consistency using `RunOnHistoricalDataAsync` across all strategies.
5. Performance reporting hooks for entry latency and post-entry MAE/MFE.

## Glossary
- EHMA: A custom efficiency-adjusted Hull Moving Average variant (current implementation: 2 * EMA(short) - EMA(long)).
- FVG (Fair Value Gap): Price displacement leaving an unfilled zone between non-overlapping candle extremes.
- Pivot High/Low: Local extrema validated by surrounding lookback range.
- Divergence: Indicator momentum direction disagrees with price direction.

---
If any strategy needs deeper detail (parameters, risk model, exit logic), a follow-up per-strategy spec can be added under `docs/strategies/detail/`.
