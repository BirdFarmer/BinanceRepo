**Pre‑Flight Market Check — User Guide**

Purpose
- A quick one‑shot check you run before trading. It summarizes recent market context and gives a simple recommendation: GO, CAUTION, or AVOID.

How to open
- From the app, open "Pre‑Flight Market Check" (Main menu). The window accepts up to 4 symbols (use format like `BTCUSDT`).
- Choose a timeframe (default: `5m`) and click "Run Analysis".

What you see
- Candles analyzed: number of bars used (the tool tries ~1000; fewer means less history).
- Regime / Trend: high‑level label such as Bullish or Bearish.
- Confidence: single-percent score (0–100). Higher = stronger, cleaner signal.
- Historical Context: overall direction and how smoothly the price moved.
- Right‑Now: current momentum and extension measures (RSI, ATR expansion, volume change).
- BTC Correlation: percent showing how closely the symbol moves with BTC (if applicable).
  - This is a simple correlation of recent price returns between the symbol and `BTCUSDT`.
  - Positive values mean the symbol tends to move with BTC; negative means it tends to move opposite BTC.
  - The help dialog shows the matched sample count (n=...) so you can see how many bars were actually compared — small n reduces reliability.
  - Rough interpretation: >=70% strong, 30–70% moderate, <30% weak (use as a directional cue, not a sole decision).
- Recommendation: simple action cue
  - ✅ GO — strong signal and directional regime
  - ⚠️ CAUTION — mixed or medium confidence
  - ❌ AVOID — weak or unclear signal

How to interpret
- Use the recommendation as a quick filter, not a trade instruction. Check candles analyzed and confidence first.
- If `Candles analyzed` is low, the historical context is unreliable — prefer CAUTION.
- High ATR expansion or extreme RSI means the instrument may be extended; consider waiting for a pullback.

Quick troubleshooting
- If the tool shows "No kline data returned" or zero candles:
  - Verify symbol format (uppercase, e.g. `BTCUSDT`).
  - Check your network and Binance access.
- If recommendations feel off, check `Confidence` breakdown and `Candles analyzed`.

Contact & feedback
- For issues or suggestions, open an issue in the repo or contact the maintainer.

This guide is intentionally brief — click the help (ℹ) icon anytime for this page.

**Metric Explanations**

- **BTC Correlation:** Pearson correlation (displayed as a percentage) computed on recent price returns between the symbol and `BTCUSDT`.
  - Positive means the symbol tends to move with BTC; negative means it tends to move opposite BTC.
  - The UI appends the sample count (`n=...`) showing how many bars were actually compared — treat correlations computed on small `n` as unreliable.
  - Rough interpretation:
    - >= 70% — strong correlation (moves with BTC)
    - 30%–70% — moderate correlation
    - < 30% — weak or negligible correlation
  - Caveats: correlation is a historical, symmetric measure. It does not imply causation and can change quickly during regime shifts.

- **ATR (Average True Range) / Expansion (ATRs):**
  - ATR measures average price range (volatility) over a lookback window. Expansion reported in the card is how far price has moved relative to ATRs (i.e., price extension measured in ATR units).
  - Interpretation:
    - Small (< 1 ATR): price is close to average move — less stretched.
    - 1–2 ATRs: moderately extended — be cautious entering at extremes.
    - > 2 ATRs: strongly extended — higher chance of pullback or mean reversion.

- **RSI(14):** 14‑period Relative Strength Index (momentum oscillator from 0–100).
  - > 70 — commonly considered overbought (short-term pullback possible).
  - < 30 — commonly considered oversold (short-term bounce possible).
  - Use RSI together with trend and ATR: an overbought reading inside a strong uptrend is different from overbought in a ranging market.

- **Volume ratio:** last candle volume ÷ average volume (on the analyzed range).
  - > 1 — higher-than-average volume on the last bar (can confirm strength of a move).
  - < 1 — lower-than-average volume (move may be weak or illiquid).

- **Candles analyzed:** number of bars used for the analysis. The tool attempts ~1000 bars when available.
  - Fewer candles → less historical evidence; treat recommendations more conservatively.

- **Trend Stage:** a simple heuristic describing where the current price sits relative to short/long EMAs (Early / Mid / Late).
  - Early: price recently moved above/below short-term EMA — early in the trend.
  - Mid: trend has established but not fully extended.
  - Late: price is extended relative to EMAs — greater risk of pullback.

- **Confidence and its components:** the displayed Confidence is a combined percent derived from two sub-scores:
  - **Trend Confidence** — how clean and strong the trend signals are (EMA alignment, directional momentum).
  - **Volatility Confidence** — whether volatility and price action are behaving in ways that make trend signals reliable.
  - The combined Confidence is meant as a quick filter: high confidence strengthens the recommendation; low confidence downgrades it.

How to use these numbers together
- Start by checking `Candles analyzed` and `Confidence`. If either is low, default to `CAUTION`.
- Use **RSI** and **Expansion (ATRs)** to judge whether price is stretched; avoid entering at high ATR expansions without a clear trend signal.
- If **BTC Correlation** is strong and you trade altcoins, align position sizing and directional bias with BTC where appropriate.
- Treat the `Recommendation` as a short-hand — always cross-check with price structure (support/resistance) and your risk plan.

**Interpreting display numbers & quick examples**

- **Correlation display**
  - Correlation values in the UI are presented as percentages (e.g. `62%`). If you ever see a raw decimal (e.g. `0.62`) treat it as `0.62 * 100 = 62%`.
  - Example: `62%` correlation means the symbol's recent returns have a fairly strong positive linear relationship with BTC.

- **ATR Expansion (can be negative)**
  - Expansion is computed as: (Price - EMA200) / ATR. It can be negative when price is below the EMA200.
  - Example: `Expansion (ATRs): -0.05` means price is 0.05 ATRs below the 200‑EMA (a very small distance — essentially flat).
  - Larger positive values (e.g. `> 1`) indicate price is extended above the 200‑EMA by that many ATRs.

- **Volume ratio → percent difference from average**
  - The `Volume ratio` shown as `Xx` is `last_candle_volume / average_volume`. To convert to percent difference from average use `(ratio - 1) * 100`.
  - Example: `0.38x` → `(0.38 - 1) * 100 = -62%`, i.e. the last candle's volume is 62% below the average volume over the analyzed range.

- **Putting it together (example from your output)**
  - `TRBUSDT` shows `Expansion (ATRs): -0,05` and `Volume ratio: 0,38x`:
    - Price is marginally below the 200‑EMA (−0.05 ATRs) and volume on the latest candle is 62% below average — this is weak participation and argues for caution despite a neutral regime.

These conversions and examples should make the numeric fields easier to read at a glance.
