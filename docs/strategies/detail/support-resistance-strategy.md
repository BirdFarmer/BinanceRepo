# SupportResistanceStrategy

- Code: `BinanceTestnet/Strategies/SupportResistanceStrategy.cs`
- Goal: Breakout + retest entries around algorithmic pivot S/R with volume and trend validation.

## Indicators & Data
- Pivot highs/lows (lookback)
- Volume SMA(20) with multiplier
- ADX(14) trend strength
- Engulfing pattern heuristic for entries

## Long entries — where and why
- Closed candle breaks above recent resistance with high volume (relative to SMA*multiplier) and adequate ADX
- Retest: subsequent candle wicks into broken resistance with prior candle above level and low retest volume
- Rationale: avoid chasing the breakout; enter on controlled retest.

## Short entries — where and why
- Closed candle below support with high volume + ADX
- Retest into broken support with low retest volume
- Rationale: symmetric bearish continuation setup.

## Notes
- Maintains internal breakout state to avoid duplicate entries.
- Exits handled centrally by order/position manager.
