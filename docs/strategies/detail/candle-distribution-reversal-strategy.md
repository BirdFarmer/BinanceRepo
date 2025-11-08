# CandleDistributionReversalStrategy

- Code: `BinanceTestnet/Strategies/CandleDistributionReversalStrategy.cs`
- Goal: Detect exhaustion/distribution then trade the ensuing reversal.

## Indicators / Data
- Candle body/upper/lower wick ratios
- Cluster statistics of narrow vs. wide ranges
- Optional volume confirmation

## Long entries — where and why
- Series of selling/narrow bodies followed by strong bullish reversal closing near highs
- Rationale: supply absorption then bullish shift.

## Short entries — where and why
- Series of buying/narrow bodies followed by strong bearish reversal closing near lows
- Rationale: demand exhaustion then bearish shift.
