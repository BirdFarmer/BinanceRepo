# MACDDiversionStrategy (MACD Divergence)

- Code: `BinanceTestnet/Strategies/MACDDiversionStrategy.cs`
- Goal: Trade reversals indicated by divergence between price and MACD momentum.

## Indicators
- MACD lines/histogram
- Swing high/low detection

## Long entries — where and why
- Bullish divergence: price lower low, MACD higher low
- Confirmation: MACD bullish cross
- Rationale: momentum bottoms ahead of price.

## Short entries — where and why
- Bearish divergence: price higher high, MACD lower high
- Confirmation: MACD bearish cross
- Rationale: momentum tops ahead of price.

## Notes
- Confirmation reduces false divergence triggers.
