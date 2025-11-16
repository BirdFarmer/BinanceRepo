# Issue: Improve RSI-based strategies — better entries, tests, and Pine parity

**Summary**

The repository contains two RSI-based strategies: `RSIMomentumStrategy` and `RsiDivergenceStrategy`. Both produce entry signals but would benefit from improved entry filters, parameterization, unit tests, backtest comparisons, and parity with TradingView Pine scripts so signals are reproducible outside the engine.

**Goals**

- Improve signal precision (reduce false entries) while preserving edge captures.
- Parameterize key hyperparameters (RSI length, thresholds, stochastic lengths, lookback) and expose them for backtests.
- Create TradingView Pine scripts that mirror the engine logic for visual validation and to compare live vs. strategy outputs.
- Add unit tests and historical backtests to validate behavior and prevent regressions.
- Add risk controls and configurable exit rules to reduce large drawdowns.

**Tasks**

1. Parameterization
   - Move thresholds (29,71,10,90, RSI & Stoch lengths, lookbacks) into strategy-config variables and/or `StrategySettings` so they can be varied in backtests.

2. Divergence detection improvements
   - Replace the current simplistic lookback scanning with a deterministic pivot detection approach (or an indexed swing-high/low builder) to avoid ambiguous pivot pairs.
   - Add tests that assert which candle-pair is selected as the signal pair for a small crafted series of klines.

3. Momentum strategy improvements
   - Add a minimum momentum / volume filter and optional confirmation from faster RSI or price action.
   - Prevent re-entries for a configurable cooldown period or until the opposite signal occurs.

4. Pine Script parity & validation
   - Finalize and maintain the Pine v5 scripts added to `Strategies/*.md` so that a developer can load the script in TradingView and visually compare signals.
   - Add a small script to export TradingView signal timestamps (manually) and a parser to compare with engine signals for a chosen symbol/timeframe.

5. Tests & Backtests
   - Add unit tests for `IsBullishDivergence`, `IsBearishDivergence`, `EvaluateRSIConditions`, and `InitializeRSIState` using crafted/synthetic Kline sequences.
   - Add regression/backtest checks that run `RunOnHistoricalDataAsync` against sample historic files and assert expected number of trades and basic P/L ranges.

6. Risk & exit rules
   - Add configurable stop-loss, take-profit, and trailing stop support at order entry.
   - Improve `OrderManager.CheckAndCloseTrades` tests to ensure predictable closing behavior.

**Acceptance criteria**

- Strategies expose parameters to configure RSI/stochastic lengths, thresholds, lookback periods.
- Pine scripts are present in `Strategies/*.md` and produce visually similar signals to the engine for a chosen sample symbol/timeframe.
- Unit tests cover divergence detection and momentum state transitions.
- A documented backtest run (README or script) shows before/after metrics and demonstrates improved entry quality.

**Notes & suggestions**

- Start by parameterizing values and adding small unit tests. This reduces risk when refactoring divergence detection.
- Use a deterministic pivot/swing detection algorithm (e.g., fixed lookback pivot) to select signal pairs reproducibly.
- Consider adding an integration test that runs a short historical dataset through each strategy and outputs a CSV of signals for manual inspection.

**Labels:** enhancement, strategy, tests, backtest

**Assignees:** @TODO
