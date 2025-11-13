**Report Guide**

- **Purpose:**: Explain how to read the HTML trading report produced by the repo and where to change behavior.

**Quick Start**
- **Open report:**: Open the generated HTML file in a browser (Chrome or Edge recommended) located under the configured `Reports` folder.
- **Generate report:**: Use the app or test harness that calls `HtmlReportGenerator.GenerateHtmlReport(sessionId, settings)`.

**Executive Summary**
- **Overall Performance:**: High-level label (PROFITABLE / UNPROFITABLE). Check Net PnL and ROI for the numeric view.
- **ROI:**: Return on Investment reported in the Executive Summary. Use this to compare sessions independent of absolute dollar amounts.
- **Risk-Adjusted Score:**: A combined measure used to show strategy performance accounting for risk. It blends factors such as win rate, drawdown and volatility. Treat it as a relative index (higher is better). If unclear for a session, inspect the Risk Analysis section for the components (Max Drawdown, Win Rate, Volatility).
- **Market Alignment:**: A simple indicator of how the session aligned with the market regime used for the report (Bullish/Bearish/Ranging). It is computed from the session reference symbol (most-traded symbol for the session) and the session timeframe.

**Session Market State (compact, one-line)**
- **What it shows:**: A short, deterministic summary for the session based on the session's reference symbol. Example:
  - `Symbol: BTCUSDT` - the symbol used to compute the market state (typically the session's most-traded symbol).
  - `State: Bullish / Bearish / Ranging` - high-level label derived from percent change, slope and volatility heuristics.
  - `Basis:` - one-line numeric explanation (percent change from start to end, slope sign, and approximate volatility).
- **How it's computed:**
  - `sessionPct = (lastPrice - startPrice) / startPrice * 100`
  - `slope` is a simple linear-regression slope on available price points from trades (entry/exit prices).
  - `volatility` is the stddev of log returns (approximate ATR/price).
  - Decision thresholds (defaults): Bullish if sessionPct >= +0.8% and slope > 0; Bearish if sessionPct <= -0.8% and slope < 0; Ranging if volatility < 0.4% or |sessionPct| < 0.2%.

**Reference Symbol / How it is picked**
- The report uses the session's most-traded symbol (by trade count, then by notional) as the reference symbol. This keeps the report data-driven and avoids assuming BTC.
- You can override the default symbol by setting `ReportSettings.SessionReferenceSymbol` in the session settings.

**Market Regime Timeline**
- **What changed:**: The timeline no longer assumes BTC only - it is computed for the session's reference symbol (see above).
- **What it shows:**: Time-sliced regime segments (period, regime type, trade counts, PnL, and a short insight). Use it to see how market regimes evolved across the session for the chosen symbol.
- **Readability tips:**
  - The timeline can be long for long sessions. Focus on segments with higher trade counts or larger PnL impact.
  - The 'Insight' column is a short automated hint (for example, "Strong performance" or "Consider avoiding this regime"). Treat it as a starting point for investigation, not a definitive trading rule.

**Strategy vs Regime Heatmap**
- **What it shows:**: A compact matrix showing how strategies performed across detected market regimes for the reference symbol. Colors map to win rate bands (green/yellow/red).
- **Interpretation:**: Use the heatmap to identify regimes where a strategy tends to perform well or poorly. Pay attention to counts - a 49% WR with 200 trades is more informative than 49% on 5 trades.

**Actionable Insights**
- Auto-generated suggestions (top 5) based on trade patterns and regime correlation. Treat suggestions as hypotheses to validate, not prescriptive rules.

**Risk Analysis (improved)**
- **Includes:** Liquidation thresholds, near-liquidations, max loss, win rate, ROI, and volatility indicators. Use this section to understand tail risk and how volatility impacted the session.

**What was removed / simplified**
- The previous large BTC-centric context block has been removed. The report is now driven by the session's most relevant symbol so the dashboard is clearer for multi-pair sessions.
- The "Market Session Performance - Realistic Limited Trades" section has been removed to reduce noise. It can be restored in code if desired.

**How to tune behavior**
- Edit `BinanceTestnet/Database/ReportSettings.cs`:
  - `SessionReferenceSymbol` - fixed reference symbol (default).
  - `SessionReferenceSymbol` - fixed reference symbol (default). The report otherwise picks the session's most-traded symbol automatically.

**Quick diagnostics**
- If the Session Market State shows "Unknown" it means there was insufficient price data in trade records for the selected symbol - check that trades have `EntryPrice`/`ExitPrice` set, or that the analyzer can fetch klines for that period.

**Where to look next**
- `BinanceTestnet/Database/HtmlReportGenerator.cs` - code that assembles the report and contains the heuristics.
- `BinanceTestnet/MarketAnalysis/MarketContextAnalyzer.cs` - code that fetches klines and derives regime segments.

**Contact / Notes**
- If you want different heuristics (for example, use ATR instead of log-return volatility, or change thresholds), tell me which rule to change and I can make it configurable via `ReportSettings`.
**Report Guide**

- **Purpose:** Explain how to read the HTML trading report produced by the repo and where to change behavior.

**Quick Start**
- **Open report:** Open the generated HTML file in a browser (Chrome or Edge recommended) located under the configured `Reports` folder.
- **Generate report:** Use the app or call `HtmlReportGenerator.GenerateHtmlReport(sessionId, settings)` from a small runner if you need to reproduce reports locally.

**Executive Summary**
- **Overall Performance:** High-level label (PROFITABLE / UNPROFITABLE). Check Net PnL and ROI for the numeric view.
- **ROI:** Return on Investment reported in the Executive Summary. Use this to compare sessions independent of absolute dollar amounts.
- **Risk-Adjusted Score:** A combined measure that blends win rate, drawdown and volatility. Treat it as a relative index (higher is better). If unclear for a session, inspect the Risk Analysis section for the components (Max Drawdown, Win Rate, Volatility).

**Session Market State (compact, one-line)**
- **What it shows:** A short, deterministic summary for the session based on the session's reference symbol. Example shown in the report header:
  - `Symbol: DASHUSDT` — the symbol used to compute the market state (typically the session's most-traded symbol).
  - `State: Bullish / Bearish / Ranging` — high-level label derived from percent change, slope and volatility heuristics.
  - `Basis:` — one-line numeric explanation (percent change from start to end, slope sign, and approximate volatility).
- **How it's computed:**
  - `sessionPct = (lastPrice - startPrice) / startPrice * 100`
  - `slope` is a simple linear-regression slope on available price points from trades (entry/exit prices).
  - `volatility` is the stddev of log returns (approximate ATR-like value expressed as %).
  - Decision thresholds (defaults): Bullish if sessionPct >= +0.8% and slope > 0; Bearish if sessionPct <= -0.8% and slope < 0; otherwise Ranging. These thresholds are intentionally conservative and can be tuned.

**Reference Symbol / How it is picked**
- The report auto-selects the session's most-traded symbol (by trade count, then by notional) as the reference symbol. This avoids assuming BTC for multi-pair sessions.
- You can override the default symbol by setting `ReportSettings.SessionReferenceSymbol` in `BinanceTestnet/Database/ReportSettings.cs`.

**Market Regime Timeline**
- **What changed:** The timeline is computed for the session's reference symbol (explicitly shown in the timeline header).
- **What it shows:** Time-sliced regime segments (period, regime type, your trades in that segment, performance and a short insight). Focus on segments with higher trade counts or larger PnL impact.

**Removed / Simplified items**
- The large BTC-centric market-context block was removed and replaced by the compact `Session Market State` to reduce noise and make the report multi-pair friendly.
- The Strategy vs Regime heatmap was removed to improve clarity (it was noisy for many sessions).
- The previously included simulated "Realistic Limited Trades" section was removed; it can be restored in code if needed.

**Risk Analysis**
- Includes liquidation thresholds, near-liquidations, max loss, win rate, ROI and volatility indicators. Use this to understand tail risk and volatility impact for the session.

**Quick diagnostics**
- If the Session Market State shows `Unknown` it means there was insufficient price data for the selected symbol. Check that trades have `EntryPrice`/`ExitPrice` set, or ensure `MarketContextAnalyzer` can fetch klines for the session timeframe.

**Tuning and configuration**
- Edit `BinanceTestnet/Database/ReportSettings.cs` to change defaults:
  - `SessionReferenceSymbol` — set a fixed symbol to override auto-selection.
  - Consider making thresholds configurable in `ReportSettings` if you want fine-grained control over Bullish/Bearish/Ranging logic.

**Where to look in code**
- `BinanceTestnet/Database/HtmlReportGenerator.cs` — assembles the HTML and contains the session-state heuristics and timeline wiring.
- `BinanceTestnet/MarketAnalysis/MarketContextAnalyzer.cs` — fetches klines and derives regime segments for a given symbol and timeframe.

**Notes**
- Header styling: a recent CSS fix restored the report header background color for better readability.
- Developer tooling: a small temporary `tools/ReportRunner` was used during development and has been removed; it is not required to generate reports from the app.

---
If you'd like thresholds, volatility measures, or the session-symbol selection to be configurable, I can make those settings available in `ReportSettings` and wire them into `HtmlReportGenerator`.
