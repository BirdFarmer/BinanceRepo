# FVGStrategy (Fair Value Gap)

- Code: `BinanceTestnet/Strategies/FVGStrategy.cs`
- Goal: Trade institutional displacement/imbalance zones via FVG identification and retests.

## Concepts & Indicators
- Fair Value Gap: Candle1 high < Candle3 low (bullish) or Candle1 low > Candle3 high (bearish)
- Order book bucketing + imbalance (bids vs. asks) via StrategyUtils

## Long entries — where and why
- After two recent bullish FVGs of same type
- Price retests the upper boundary of the nearest bullish FVG from below
- Order book shows meaningful bid imbalance
- Rationale: continuation from liquidity gap with supportive flow.

## Short entries — where and why
- Mirror conditions for bearish gaps with ask-side dominance.

## Notes
- Signals should reference last closed candle; imbalance helps filter weak gaps.
- Exits via shared trade manager.
