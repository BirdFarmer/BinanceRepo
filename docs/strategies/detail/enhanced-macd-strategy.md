# EnhancedMACDStrategy

- Code: `BinanceTestnet/Strategies/EnhancedMACDStrategy.cs`
- Goal: Momentum confirmation via MACD, guided by EMA trend and RSI sanity checks.

## Indicators
- MACD (fast/slow/signal)
- EMA (trend bias)
- RSI (overbought/oversold guard)

## Long entries — where and why
- MACD bullish cross / histogram turns positive
- Fast EMA above slow EMA
- RSI not in extreme overbought
- Rationale: stack momentum + trend and avoid exhaustion entries.

## Short entries — where and why
- MACD bearish cross / histogram negative
- Fast EMA below slow EMA
- RSI not in extreme oversold
- Rationale: mirror on downside to avoid late shorts.

## Notes
- Use last closed candle for signals to avoid repainting.
- Exits are centralized in position management.
