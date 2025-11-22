**Overview**:
- **Purpose**: Quick, one-shot market-regime summary used before trading live. The Pre‑Flight tool evaluates up to 4 coin symbols in parallel and presents: historical context (≈1000 candles), right-now metrics, BTC correlation, candles analyzed, and a simple recommendation (GO / CAUTION / AVOID).

**Where the code lives**:
- **UI / orchestrator**: `TradingAppDesktop/Views/PreFlightMarketCheckWindow.xaml.cs`
- **Kline fetch helper used by UI**: `TradingAppDesktop/Services/BinanceTradingService.cs` (method: `FetchHistoricalDataPublic`)
- **Regime analysis**: `BinanceTestnet/MarketAnalysis/MarketContextAnalyzer.cs`
- **Kline model**: `BinanceTestnet/Models/Kline.cs`
- **Indicator helpers**: `BTCTrendCalculator` (indicator calculations used by the UI and analyzer)

**Data source & fetch behavior**:
- The canonical endpoint used is Binance Futures klines: `/fapi/v1/klines` (base `https://fapi.binance.com`).
- Lookback behavior:
  - The UI attempts recent-only lookbacks first: 100, 250, 500, 1000 candles (using `startTime` to request a time range).
  - If that returns no data (exchange sometimes returns only more recent candles), the UI falls back to a latest-N request using `limit=1000` with no `startTime` and parses the response.
- The UI reuses the same fetch helper that strategies use, so signals match strategy logic.

**Terminology & metrics shown**:
- **Candles analyzed**: Number of kline/candles actually retrieved and used for the analysis (N). If this is significantly less than ~1000, interpret historical context with caution.

- **Regime / Trend (Type)**:
  - A high-level label computed by `MarketContextAnalyzer` (e.g., `BullishTrend`, `BearishTrend`, `Range`, etc.). Represents the current classified market regime.

- **Confidence**:
  - Overall confidence is reported as a percentage (0–100).
  - Implementation: `OverallConfidence` = average of `TrendConfidence` and `VolatilityConfidence` inside `MarketContextAnalyzer`.
  - **TrendConfidence**: measures multi-timeframe alignment, EMA spreads, and RSI momentum. Higher means the trend signal is stronger and cleaner.
  - **VolatilityConfidence**: measures volatility regime suitability (ATR, dispersion). High volatility confidence indicates predictable/consistent volatility for the regime.
  - Interpretation:
    - >= 75%: strong signal (used by the UI mapping for `GO` when regime is directional)
    - 50–74%: medium (UI shows `CAUTION`)
    - <50%: weak (UI shows `AVOID`)

- **Trend Strength Score**:
  - A normalized 0–1 score derived from trend-alignment heuristics (higher is stronger trend).
  - Use to compare trend clarity across symbols.

- **Direction & Change (%)**:
  - Percentage price change from the first to the last candle in the historical sample.
  - Use to gauge how much the instrument has moved over the sampled window.

- **Trend Quality (Efficiency)**:
  - Computed as net change divided by sum of absolute moves. Range 0–1 where values closer to 1 indicate smoother directional movement (less choppy).

- **BTC Correlation**:
  - Simple Pearson-like correlation computed on returns between the target and `BTCUSDT` across overlapping samples. Shown as a percent (e.g., `+85%` means strong positive correlation).
  - If `--` shown: symbol is `BTCUSDT` itself or insufficient overlap.
  - Interpretation: High positive correlation means the coin tends to move with BTC; negative means inverse movement.

- **Right-Now metrics**:
  - **Trend Stage**: heuristic bucket (Early / Mid / Late) computed from current price relative to EMA50/EMA200 distance. Helps gauge where we might be inside a trending move.
  - **RSI(14)**: momentum indicator (numeric). Common thresholds: >70 = overbought, <30 = oversold.
  - **Expansion (in ATRs)**: distance from EMA200 measured in ATR units (how far price is extended relative to recent volatility). Large values imply the price is stretched.
  - **Volume (last vs avg)**: ratio of the last candle's volume to the historical average (e.g., `1.8x` means last candle had 80% more volume than average).

- **Recommendation mapping (UI)**:
  - `✅ GO` — shown when `OverallConfidence >= 75` and regime is directional (Bullish or Bearish).
  - `⚠️ CAUTION` — shown when `OverallConfidence >= 50` but below the `GO` threshold.
  - `❌ AVOID` — shown when `OverallConfidence < 50`.
  - Color-coding: `GO` = light green card, `CAUTION` = light yellow, `AVOID` = light coral.

**How to reproduce the analysis programmatically**:
- Use `FetchHistoricalDataPublic(client, symbol, timeframe, start, end)` to get `List<Kline>`.
- Compute the indicator set with `BTCTrendCalculator` helpers (EMA50/100/200, RSI(14), ATR).
- Create a `BTCTrendAnalysis` with the primary indicator set, pass it and the klines to `MarketContextAnalyzer.AnalyzeCurrentRegime(...)`.
- Read `MarketRegime` fields for `TrendConfidence`, `VolatilityConfidence`, `OverallConfidence`, `TrendStrength`, `PriceVs200EMA`, `RSI`, `ATRRatio`, etc.

**Troubleshooting**:
- "No kline data returned" or very small `Candles analyzed`:
  - Exchange sometimes returns only recent candles when `startTime` is before their stored range. The UI attempts progressively smaller lookbacks (100, 250, 500, 1000) and finally `limit=1000` with no `startTime`.
  - If you still see no data, check network connectivity, rate-limiting, or symbol correctness (use uppercase `BTCUSDT` format).
- Wrong/confusing recommendations:
  - Verify `candles analyzed` — low N will reduce reliability.
  - Check `Confidence` breakdown (Trend vs Vol) to see which side is low.
- To debug the raw response, open `TradingAppDesktop/Views/PreFlightMarketCheckWindow.xaml.cs` and inspect the diagnostic message returned when klines are missing (it includes HTTP status and a short content length).

**Build & run (quick)**
- From the repo root:
```powershell
cd C:\Repo\BinanceAPI
dotnet build .\BinanceAPI.sln -c Debug
```
- Launch the WPF app in Visual Studio or run the `TradingAppDesktop` project.

**Limitations & next steps**:
- The simple BTC correlation uses overlapping returns and requires similar-length samples; mismatched candle counts reduce accuracy.
- Recommendation thresholds are intentionally conservative; adjust thresholds in `PreFlightMarketCheckWindow.xaml.cs` or centralize them in `MarketContextAnalyzer` if you want different behavior.
- Performance: consider adding a DB-first cache (use `DatabaseManager`) for repeated runs to avoid repeated network fetches.
- Testing: add unit tests for `ParseKlinesFromContent`, indicator calculations, and `MarketContextAnalyzer` edge cases.

**Contact points in code to change behavior**:
- Change the lookback or fallback strategy: `TradingAppDesktop/Views/PreFlightMarketCheckWindow.xaml.cs` (within `AnalyzeSymbolAsync`).
- Change recommendation thresholds or mapping: same file (UI mapping) or move logic to `MarketContextAnalyzer` for centralization.
- Add caching: modify `TradingAppDesktop/Services/BinanceTradingService.cs` to consult DB first, or add a wrapper in the UI to call `DatabaseManager` before network.

If you'd like, I can also:
- Add this doc to the repo (done), or further expand it with visuals (example card screenshot) or a quick reference table for values.
- Run a local build here and list any remaining compile errors (I can attempt that next).