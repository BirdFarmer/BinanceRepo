# BollingerSqueezeStrategy

- Code: `BinanceTestnet/Strategies/BollingerSqueezeStrategy.cs`
- Goal: Trade volatility expansions after periods of contraction (“squeeze”).

## What defines a "squeeze" here?
This implementation measures contraction by comparing current Bollinger Band width to the Average True Range (ATR):

- Bollinger Bands: period = 25, stdDev = 1.5
- ATR: period = 14
- Band width = UpperBand − LowerBand
- Squeeze condition: Band width < ATR × squeezeThreshold
	- Default squeezeThreshold = 2.0 (lower → easier to trigger, more squeezes)
- A counter tracks consecutive candles in squeeze. A “valid” squeeze is defined as ≥ 3 bars in squeeze (minSqueezeBars = 3), but today’s live entry logic triggers on any squeeze bar (the ≥ 3 rule is computed but not enforced for entries).

Why this metric? Using ATR normalizes band width for current volatility. When width is small relative to ATR, the market is compressed and more likely to expand.

## Indicators
- Bollinger Bands(25, 1.5)
- ATR(14)

## Entry behavior: breakout or retest?
Current behavior is breakout-only during a squeeze:
- Long: The latest close crosses above the Upper Band while the prior close was at/below the Upper Band.
- Short: The latest close crosses below the Lower Band while the prior close was at/above the Lower Band.

No retest is required in the current code. If you prefer a retest, two common options are:
- Retest-to-band: After the initial breakout, wait for a pullback to the breached band (Upper for longs, Lower for shorts), then enter on the next close away from the band.
- Retest-to-mid: After breakout, wait for a pullback toward the middle band (SMA basis) that holds, then enter on resumption.

These retest modes can be added behind a configuration switch (e.g., `RequireRetest = false/true`, `RetestTo = Band|Middle`, `RetestLookback = N`).

## Long entries — where and why (current)
- Condition: In-squeeze AND last close > Upper Band AND previous close ≤ Upper Band.
- Rationale: Price escapes compression to the upside; immediate participation aims to capture the initial expansion leg.

## Short entries — where and why (current)
- Condition: In-squeeze AND last close < Lower Band AND previous close ≥ Lower Band.
- Rationale: Mirror of long side; capture downside volatility release.

## Parameters (defaults)
- BB period = 25
- BB stdDev = 1.5
- ATR period = 14
- Squeeze threshold = 2.0
- Min squeeze bars (computed) = 3 (not enforced for entry in current version)

## Notes
- Using the last and previous closes; consider enforcing strictly closed-candle operation if running on live candles to avoid intrabar flips.
- You can tighten or loosen squeeze detection via `squeezeThreshold`. For fewer but higher-conviction squeezes, reduce stdDev to 1.0–1.5 and keep threshold modest (e.g., 1.0–1.5), and/or require `minSqueezeBars`.
