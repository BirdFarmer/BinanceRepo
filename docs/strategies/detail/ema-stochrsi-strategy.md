# EmaStochRsiStrategy

- Code: `BinanceTestnet/Strategies/EmaStochRsiStrategy.cs`
- Goal: Time entries within a trend using StochRSI crosses.

## Indicators
- EMA fast/slow
- StochRSI (%K/%D)

## Long entries — where and why
- Fast EMA > slow EMA
- %K crosses above %D from oversold territory
- Rationale: momentum alignment within trend direction.

## Short entries — where and why
- Fast EMA < slow EMA
- %K crosses below %D from overbought
- Rationale: downside continuation with timing aid.
