# AroonStrategy

- Code: `BinanceTestnet/Strategies/AroonStrategy.cs`
- Goal: Catch emerging trend acceleration using Aroon momentum, SMA(200) context, and an EHMA crossover trigger.

## Indicators
- Aroon(20)
- SMA(200)
- EHMA(70) – computed via StrategyUtils

## Long entries — where and why
- EHMA turns up (current EHMA > previous, prior state non-bullish)
- Price is below the rising SMA(200) and pressing back toward it (mean reversion room)
- Aroon suggests upward momentum (internal helper currently simplified)
- Rationale: enter early into momentum shift with supportive higher-timeframe context.

## Short entries — where and why
- EHMA turns down (current < previous) with prior state non-bullish to bullish
- Price is above a falling SMA(200) and rolling back toward it
- Aroon suggests downtrend momentum
- Rationale: downside acceleration with mean reversion potential.

## Parameters
- Aroon period: 20
- SMA period: 200
- EHMA length: 70

## Notes
- Closed-candle processing preferred (audit in Phase 2/3).
- Exits are managed externally by the order/position management services.
- Future: refine Aroon cross logic and ensure strict closed-candle enforcement.
