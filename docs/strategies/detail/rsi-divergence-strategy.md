# RsiDivergenceStrategy

- Code: `BinanceTestnet/Strategies/RsiDivergenceStrategy.cs`
- Goal: Detect reversals via RSI divergence, often earlier than MACD.

## Indicators
- RSI(14 default)
- Swing structure

## Long entries — where and why
- Bullish divergence: price lower low, RSI higher low
- RSI exits oversold region
- Rationale: early reversal indication with improving momentum.

## Short entries — where and why
- Bearish divergence: price higher high, RSI lower high
- RSI exits overbought
- Rationale: early topping signal.
