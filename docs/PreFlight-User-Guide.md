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
