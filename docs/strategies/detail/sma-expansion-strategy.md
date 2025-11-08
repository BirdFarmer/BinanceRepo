# SMAExpansionStrategy

- Code: `BinanceTestnet/Strategies/SMAExpansionStrategy.cs`
- Goal: Trade volatility/momentum expansion around a baseline SMA.

## Indicators
- Fast SMA vs. long SMA
- (Optional) ATR/range measures

## Long entries — where and why
- Fast SMA accelerates above long SMA; price pulls back shallowly and resumes higher
- Rationale: catch expansion phase early.

## Short entries — where and why
- Fast SMA accelerates below long SMA; shallow pullback fails and resumes down
- Rationale: downside expansion.
