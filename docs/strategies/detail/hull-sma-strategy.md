# HullSMAStrategy

- Code: `BinanceTestnet/Strategies/HullSMAStrategy.cs`
- Goal: Identify trend transitions using dual EHMA lengths (fast vs. slow) for reduced lag.

## Indicators
- EHMA(short=20)
- EHMA(long=100)
- (Optional) RSI filter in extended variants

## Long entries — where and why
- Fast EHMA crosses above slow EHMA
- Often accompanied by improving momentum context
- Rationale: crossover of smoothed, low-lag averages aims to catch early trend flips.

## Short entries — where and why
- Fast EHMA crosses below slow EHMA
- Rationale: symmetric bearish transition signal.

## Parameters
- Fast length: 20
- Slow length: 100

## Notes
- Signals generated on closed candles recommended (enforced progressively in Phase 2).
- Exits handled by shared order management.
