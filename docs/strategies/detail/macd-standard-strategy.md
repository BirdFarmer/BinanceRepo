# MACDStandardStrategy

- Code: `BinanceTestnet/Strategies/MACDStandardStrategy.cs`
- Goal: Classic MACD momentum rotation with optional trend filter.

## Indicators
- MACD (12,26,9 typical)
- Optional SMA/EMA context

## Long entries — where and why
- MACD line crosses above signal; histogram flips positive
- Optional: price above trend filter
- Rationale: first leg of bullish rotation.

## Short entries — where and why
- MACD line crosses below signal; histogram negative
- Optional: price below trend filter
- Rationale: momentum turning down.

## Notes
- Favor closed candles to avoid signal flicker.
