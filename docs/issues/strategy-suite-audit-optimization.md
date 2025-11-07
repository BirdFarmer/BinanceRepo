# Strategies: Clean Up and Standardize

labels: refactor, tech-debt, trading-strategies, performance, risk, enhancement

## Summary
Strategies have diverged in style and logic (live vs historical decisions, duplicated utilities, namespace inconsistencies, and some signal bugs). This issue unifies namespaces, improves signal correctness (closed-candle standard, Aroon fix), centralizes configuration, reduces duplication, and enhances logging/risk visibility.

## Why
- Eliminate confusion and fragile references caused by `BinanceLive` vs `BinanceTestnet` namespaces.
- Improve signal reliability (don’t trade on forming candles; correct Aroon down-cross).
- Reduce duplicated boilerplate (parsing, request creation, MA helpers).
- Make periods/thresholds tunable via config for faster iteration.
- Improve observability and prepare for risk framework upgrades.

## Scope
- Strategies: Aroon, EnhancedMACD, HullSMA, FVG, SupportResistance, EmaStochRsi, IchimokuCloud, MACDDivergence, MACDStandard, RsiDivergence, RSIMomentum, FibonacciRetracement, SMAExpansion, BollingerSqueeze, SimpleSMA375
- Base/helpers: `StrategyBase.cs`, `StrategyRunner.cs`
- Service consumers: `TradingAppDesktop` and `TradingAPI` services that reference strategies

## Problems Observed
- Namespace inconsistency (fixed in Phase 1 during this work): all strategies now under `BinanceTestnet.Strategies`.
- Duplicated utilities: `ParseKlines`, `CreateRequest`, and MA variants copied across files.
- Live vs historical mismatch: some live code uses the current (forming) candle; historical loops sometimes use “last of series” instead of per-index.
- Specific bugs:
  - Aroon down-cross condition mirrors up-cross (wrong comparison) in live path.
  - “Hull” logic is actually `2*EMA(n/2) - EMA(n)` (EHMA variant). Decide to keep/rename or switch to canonical HMA.
- Hard-coded parameters (RSI/ADX/periods) and inconsistent logging.
- Performance: full re-fetch and full indicator recompute each cycle.

## Proposed Changes
- Extract shared helpers into `Strategies/Helpers/StrategyUtils.cs` (HTTP, parsing, crossovers, MA helpers).
- Enforce “last closed candle” for live decisions; align historical loops to per-index logic.
- Fix Aroon down-cross and add thresholded interpretation (Up/Down cross + 50-line checks).
- Decide on EHMA vs HMA; implement once and rename consistently.
- Centralize parameters in config (`appsettings.json` / `TradingConfig`) and inject into strategies.
- Standardize order metadata and integrate ATR-based trailing activation (via `OrderManager`).
- Replace `Console.WriteLine` with structured logging (`TradeLogger`) and a consistent schema.
- Optimize API usage and indicator recomputation (incremental append).
- Add tests: crossovers, Aroon signal, FVG zone lifecycle; smoke backtests per strategy.
- Update per-strategy docs and add a short “Strategy Contract”.

## Phases & Tasks
- [x] Phase 1: Namespace refactor → move all strategies and base/runner to `BinanceTestnet.Strategies`; update service imports
- [ ] Phase 2: Utilities extraction → `StrategyUtils` for HTTP/parsing/MA/crossovers
- [ ] Phase 3: Signal correctness → Aroon down-cross fix; closed-candle standardization
- [ ] Phase 4: Config parameterization → move periods/thresholds to `appsettings.json`/`TradingConfig`
- [ ] Phase 5: Logging & risk metadata → standardized structured logs; unified order metadata
- [ ] Phase 6: Tests → unit tests (crossovers, Aroon, FVG) + smoke backtests
- [ ] Phase 7: Performance → incremental klines/indicators; minimal recompute

## Acceptance Criteria
- Build passes with unified namespaces and resolved references
- Live strategies use last closed candle; historical loops use per-index logic
- Aroon short/long signals validated by tests (down-cross fixed)
- Shared helpers remove duplicate code in ≥3 strategies
- Configurable parameters (no code changes needed to tune periods/thresholds)
- Structured logs: { timestamp, symbol, interval, strategy, signal, price, indicators }
- Backtests run without exceptions; trades recorded consistently
- Reduced redundant recomputation and API calls

## Out of Scope
- New strategy R&D or ML
- UI redesign
- Cross-exchange integrations

## Risks & Mitigations
- Behavior changes: capture before/after metrics (trade count, win rate) and document
- Breadth of refactor: ship in phases with frequent merges
- Indicator naming: explicitly document EHMA vs HMA choice

## Dependencies
- Stable `OrderManager` API (ATR trailing activation alignment)
- Database schema unchanged (logging additions should be non-breaking)

## Definition of Done
- Phases completed; docs updated; tests green; backtests run cleanly
- Strategies consistent between live and historical
- Configurable parameters available and documented

## Branch Name Suggestion
feature/strategy-suite-cleanup-<issue-number>

## Next Steps
1) Open a GitHub issue with this content
2) Create the branch above
3) Start Phase 2 (utilities extraction) to reduce future diffs
