**Overview**:
- **Purpose**: Quick, one-shot market-regime summary used before trading live. The Pre‑Flight tool evaluates up to 4 coin symbols in parallel and presents: historical context (≈1000 candles), right-now metrics, BTC correlation, candles analyzed, and a simple recommendation (GO / CAUTION / AVOID).

**Where the code lives**:
- **UI / orchestrator**: `TradingAppDesktop/Views/PreFlightMarketCheckWindow.xaml.cs`
- **Kline fetch helper used by UI**: `TradingAppDesktop/Services/BinanceTradingService.cs` (method: `FetchHistoricalDataPublic`)
- **Regime analysis**: Pre‑Flight now builds a timeframe-local `MarketRegime` using the klines it fetches for each symbol. It does NOT call the shared `MarketContextAnalyzer` when producing the UI recommendation; the shared analyzer remains available and is still used elsewhere (reports), but Pre‑Flight shows its own local scores for transparency.
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
    - >= 75%: strong signal (this is the analyzer's guideline; Pre‑Flight uses a local, timeframe-specific score for recommendations — see below)
    - 50–74%: medium
    - <50%: weak
    - Note (2025-11-23): The Pre‑Flight UI computes a timeframe‑local confidence (shown as `Local Scores`) and uses that local score to drive recommendations. The local score is symmetric for bullish/bearish signals and is computed from two components:
      - `TrendConfidenceLocal` (0–100): EMA alignment, EMA spread strength, RSI momentum, efficiency (net/smoothness), and recent-bar confirmation.
      - `VolatilityConfidenceLocal` (0–100): ATR stability vs a recent ATR MA, ATR relative to price, volume sanity (quote-volume), and volatility stability (stddev of returns).
    - `OverallConfidenceLocal` = average(TrendConfidenceLocal, VolatilityConfidenceLocal). The UI recommendation mapping uses this local overall score by default (the analyzer's confidences are still shown for comparison).
    - Local recommendation thresholds (UI default):
      - `✅ GO` — `OverallConfidenceLocal >= 70` and local regime is directional (Bullish or Bearish)
      - `⚠️ CAUTION` — `OverallConfidenceLocal >= 50` but < 70
      - `❌ AVOID` — `OverallConfidenceLocal < 50`

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
   - **Volume (last vs avg)**: all volume metrics in the Pre‑Flight UI are reported in the quote currency (USDT) to make cross-symbol comparison meaningful. Quote-volume is computed locally as:
    - Prefer `quoteVolume` when the kline model includes it; otherwise compute `quoteVolume = baseVolume * closePrice`.
    - The UI uses the **previous closed candle** (the second‑to‑last candle in the fetched series) as the `last` closed volume to avoid counting a partially-filled, in-progress bar.
    - The card shows `Volume (last): <value> USDT  Avg: <value> USDT  Ratio: <x>x (<+/-y%>)`.

- **Recommendation mapping (UI)**:
  - `✅ GO` — shown when the UI's `OverallConfidenceLocal >= 70` and the local regime is directional (Bullish or Bearish).
  - `⚠️ CAUTION` — shown when `OverallConfidenceLocal >= 50` but below the `GO` threshold.
  - `❌ AVOID` — shown when `OverallConfidenceLocal < 50`.
  - Color-coding: `GO` = light green card, `CAUTION` = light yellow, `AVOID` = light coral.

**How to reproduce the analysis programmatically**:
- Use `FetchHistoricalDataPublic(client, symbol, timeframe, start, end)` to get `List<Kline>`.
- Compute the indicator set with `BTCTrendCalculator` helpers (EMA50/100/200, RSI(14), ATR).
- For parity with reports you can use `MarketContextAnalyzer.AnalyzeCurrentRegime(...)` (this is the shared analyzer used in reports). Note: Pre‑Flight itself constructs its own local `MarketRegime` and recommendation using the timeframe-only indicators and local scoring; it does not call the shared analyzer when producing its UI recommendation.
- Read `MarketRegime` fields for `TrendConfidence`, `VolatilityConfidence`, `OverallConfidence`, `TrendStrength`, `PriceVs200EMA`, `RSI`, `ATRRatio`, etc., if using the shared analyzer.

**Troubleshooting**:
- "No kline data returned" or very small `Candles analyzed`:
  - Exchange sometimes returns only recent candles when `startTime` is before their stored range. The UI attempts progressively smaller lookbacks (100, 250, 500, 1000) and finally `limit=1000` with no `startTime`.
  - If you still see no data, check network connectivity, rate-limiting, or symbol correctness (use uppercase `BTCUSDT` format).
- Wrong/confusing recommendations:
  - Verify `candles analyzed` — low N will reduce reliability.
  - Check `Confidence` breakdown (Trend vs Vol) to see which side is low.
- To debug the raw response, open `TradingAppDesktop/Views/PreFlightMarketCheckWindow.xaml.cs` and inspect the diagnostic message returned when klines are missing (it includes HTTP status and a short content length).

**Example Copy-Card (what you get in the clipboard)**
```
==== Pre-Flight Card ====
BTCUSDT    5m    2025-11-23 12:34 UTC
Regime: BullishTrend (Strong)
Confidence: 78% (Trend 82 / Vol 74)
-- Historical Context --
Candles analyzed: 1000
First close: 27000.12345678
Last close: 27250.98765432
Direction & Change: BullishTrend 0.93%
Trend Strength Score: 0.82 (0-1)
Trend Quality (efficiency): 0.57
-- Right Now --
Price: 27250.98765432
EMA50: 27100.1234  EMA200: 26000.5678
ATR: 150.2345
Expansion (ATRs): 0.67
Trend Stage: Mid (50%)
RSI(14): 64 (Neutral)
Volume (last): 1200000.00 USDT  Avg: 1000000.00 USDT  Ratio: 1.20x (20.0%)
Recommendation: ✅ GO
-- Local Scores --
Trend: 82  Vol: 74  Overall: 78
=========================
```

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