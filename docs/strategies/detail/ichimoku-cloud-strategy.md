# IchimokuCloudStrategy

- Code: `BinanceTestnet/Strategies/IchimokuCloudStrategy.cs`
- Goal: Multi-factor trend confirmation using Ichimoku components.

## Indicators
- Tenkan-sen, Kijun-sen, Senkou Span A/B (Cloud), Chikou span

## Long entries — where and why
- Price above cloud, Tenkan > Kijun, Chikou above price, future cloud bullish
- Rationale: broad consensus of bullish conditions.

## Short entries — where and why
- Price below cloud, Tenkan < Kijun, Chikou below price, future cloud bearish
- Rationale: consensus bearish conditions.
