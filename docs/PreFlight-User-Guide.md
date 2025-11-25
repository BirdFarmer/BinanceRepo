***Begin Update***
**Pre‑Flight Market Check — User Guide**

Purpose
- A short pre-trade check that summarizes recent market context and gives a quick recommendation: `✅ GO`, `⚠️ CAUTION`, or `❌ AVOID`.

How to open
- In the app open `Pre‑Flight Market Check`. The window accepts up to 4 symbols (use `BTCUSDT` style pairs).
- Choose timeframe (default `5m`) and click **Run Analysis**.

What you see (short)
- **Candles analyzed**: number of bars used (tool tries ~1000 when available).
- **Regime / Trend**: high-level label (BullishTrend, BearishTrend, RangingMarket, etc.).
- **Confidence**: 0–100 percent; higher = cleaner signal. Pre‑Flight shows both the shared analyzer confidences and a local, timeframe-only `Local Scores` set used for recommendations.
- **Right‑Now**: quick indicators (RSI, ATR expansion, Trend Stage, volume deltas).
- **BTC Correlation**: percent correlation vs `BTCUSDT` (diagnostic).

Important user notes
- Cards show `Local Scores` (Trend / Volatility / Overall) and a recommendation color: `✅ GO` (green), `⚠️ CAUTION` (yellow), `❌ AVOID` (red). Use these as filters, not trading instructions.
- Volumes are shown in the quote currency (USDT). The UI prefers kline `quoteVolume`; if missing it computes `quoteVolume = baseVolume * closePrice`.
- The **last** volume uses the previous closed candle (second‑to‑last) to avoid counting an in-progress bar.
- The UI includes a `Show diagnostics` toggle (default: **off**) that expands detailed diagnostics per card; diagnostics are also included in clipboard output only when enabled.
- Use the **Copy All Cards** button to copy an aggregated clipboard payload for all visible cards. If diagnostics are enabled, their detail is included.

How recommendations are produced (brief)
- Pre‑Flight computes a timeframe-local `TrendConfidenceLocal` and `VolatilityConfidenceLocal` (0–100) from the klines it fetches. `OverallConfidenceLocal` = average(Trend, Vol). The UI uses `OverallConfidenceLocal` to map to GO/CAUTION/AVOID with default thresholds:
  - `✅ GO` — Overall >= 70 and regime is directional
  - `⚠️ CAUTION` — Overall >= 50 and < 70
  - `❌ AVOID` — Overall < 50
- **Choppy override**: when `Efficiency < 0.10` and `Candles analyzed >= 100`, the UI will override directional regimes and mark `RangingMarket (Choppy)` to avoid false signals.

Volume & warnings
- Volume is reported as percent difference vs a short-term average and vs a 200-bar volume MA. Examples: `+20.0%`, `-62.0%`.
- Pre‑Flight adds a visual volume warning and includes it in clipboard output:
  - Red (High Alert): last < 30% of short-term avg (ratio < 0.30)
  - Orange (Caution): 30% ≤ last < 70% of short-term avg (0.30 ≤ ratio < 0.70)

Trend stage (ATR-based)
- Trend Stage labels are now ATR-based (Early / Mid / Extended (High Risk)) using distance from long-term baseline measured in ATRs. `Extended (High Risk)` replaces the older `Late` label.

Clipboard / Copy behavior
- Each card stores a compact, percent-first payload. `Copy` on a card copies that single payload; `Copy All Cards` aggregates every card's payload and copies a combined text blob.
- When diagnostics are enabled the clipboard includes extra parsing notes, BTC correlation, and the full local scores block.

Quick troubleshooting
- If `Candles analyzed` is very small, prefer `CAUTION` — historical context is weak.
- If no kline data appears, verify symbol format (uppercase) and network/Binance access.

Metric cheat-sheet
- **BTC Correlation** — percent correlation vs `BTCUSDT` on overlapping returns. `>=70%` strong, `30–70%` moderate, `<30%` weak. Shown with `n=` sample count.
- **ATR Expansion** — (Price − EMA200) / ATR; interprets distance in ATR units.
- **RSI(14)** — momentum (0–100). >70 = overbought, <30 = oversold.
- **Volume (vs short-term avg / vs 200-bar MA)** — percent diff from the respective baseline. Pre‑Flight displays these as percent-first values (e.g. `-62.0%`).
- **Efficiency (Trend Quality)** — net change / sum(abs(returns)), shown as percent in UI (e.g. `57%`). When < 10% and enough candles, the UI treats the market as choppy/ranging.

Examples
- Copy payloads are compact and percent-first. Example excerpt:

```
BTCUSDT  5m  2025-11-23 12:34 UTC
Regime: BullishTrend (Strong)
Confidence: 78% (Trend 82 / Vol 74)
Volume (vs short-term avg): +20.0%   Volume (vs 200-bar MA): +12.3%   Volume Warning: None
Recommendation: ✅ GO
Local Scores: Trend 82  Vol 74  Overall 78
Diagnostics: (included when enabled)
```

End of guide — use the help icon for this page in-app for the latest version.

***End Update***
