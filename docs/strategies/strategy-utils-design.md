# StrategyUtils Design (Phase 2 → Phase 3 Intro)

Audience: internal design for Strategy Suite cleanup. This document specifies the shared helpers introduced in Phase 2 and sets the stage for Phase 3 (signal stability & candle policy). Phase 2 delivered consolidation and safety guards with zero intended functional changes. Phase 3 will deliberately adjust signal timing to eliminate forming-candle noise.

Goals
- Remove duplicate utility code across strategies (HTTP requests, klines parsing, candle selection, MA helpers).
- Improve null-safety and invariants in a single place.
- Make strategies shorter, clearer, and easier to test.

Scope (helpers only; no strategy logic changes)
- HTTP request factory
- Klines JSON parsing (futures klines format)
- Quotes conversion
- Candle selection helpers (last closed, previous closed)
- Data sufficiency checks and safe indexing
- Moving average helpers (EHMA for now; HMA decision deferred to Phase 3/4)
- Optional: order book bucketing

Namespace
- Namespace: `BinanceTestnet.Strategies.Helpers`
- Class: `StrategyUtils` (static)

API Proposal

1) HTTP
- RestRequest CreateGet(string resource, IDictionary<string, string>? query = null)
  - Adds standard headers: Content-Type: application/json, Accept: application/json
  - Applies query parameters if provided
  - Returns RestRequest configured for GET

2) Parsing
- List<Kline> ParseKlines(string content)
  - Input: Binance futures klines JSON (array of arrays)
  - Behavior: Returns empty list on bad/missing content; never throws
  - Parsing: invariant culture for decimals; fields: OpenTime, Open, High, Low, Close, CloseTime, NumberOfTrades; Volume if present
- bool TryParseKlines(string? content, out List<Kline> result)
  - Returns false if content is null/invalid; result = empty list

3) Quotes conversion
- List<BinanceTestnet.Models.Quote> ToQuotes(IReadOnlyList<Kline> klines, bool includeOpen = true, bool includeVolume = true)
  - Converts Klines to Skender.Stock.Indicators Quote list (Date, Open, High, Low, Close, Volume)

4) Candle selection & guards
- bool HasEnough<T>(IReadOnlyList<T> list, int required)
- (Kline? lastClosed, Kline? prevClosed) GetLastClosedPair(IReadOnlyList<Kline> klines)
  - Returns (klines[^2], klines[^3]) if available; otherwise (null, null)
- T? LastOrDefaultSafe<T>(IReadOnlyList<T> list)
- T? ElementAtOrDefaultSafe<T>(IReadOnlyList<T> list, int index)

5) Safe parsing helpers
- decimal SafeDecimal(object? value)
  - Invariant-culture parse; returns 0 if null/invalid
- bool TryParseDecimal(object? value, out decimal result)

6) Moving average helpers
- List<(DateTime Date, decimal EHMA, decimal EHMAPrev)> CalculateEHMA(IReadOnlyList<BinanceTestnet.Models.Quote> quotes, int length)
  - Definition: 2 * EMA(n/2) - EMA(n)
  - Alignment: Returns a list with same count as quotes (pre-length items return EHMA=0, EHMAPrev=0)
  - Note: HMA vs EHMA decision deferred (Phase 3/4). For now we consolidate the existing EHMA behavior.

7) Order book helpers (optional, used by FVG)
- Dictionary<decimal, decimal> BucketOrders(List<List<decimal>> orders, int significantDigits = 4)
- decimal RoundToSignificantDigits(decimal value, int significantDigits)

Design Choices & Contracts
- Exceptions: Helpers do not throw for bad input; they return empty collections or zero values. Strategies handle absence of data.
- Nullability: Public methods avoid returning null collections. `Try*` methods use out parameters.
- Culture: Always use `CultureInfo.InvariantCulture` for parsing and formatting.
- Consistency: ParseKlines returns the same fields across strategies; quote conversion includes Open/Volume by default.
- Alignment: Indicator lists should match input count when feasible to simplify indexing inside strategies.

Usage Examples (pseudocode)
- Fetch and parse klines
  var request = StrategyUtils.CreateGet("/fapi/v1/klines", new Dictionary<string,string>{{"symbol", symbol},{"interval", interval},{"limit","401"}});
  var response = await client.ExecuteGetAsync(request);
  var klines = StrategyUtils.TryParseKlines(response.Content, out var parsed) ? parsed : new List<Kline>();
  if (!StrategyUtils.HasEnough(klines, 2)) return;

- Get last closed candle pair
  var (lastClosed, prevClosed) = StrategyUtils.GetLastClosedPair(klines);
  if (lastClosed is null || prevClosed is null) return;

- Compute EHMA once
  var quotes = StrategyUtils.ToQuotes(klines);
  var ehma = StrategyUtils.CalculateEHMA(quotes, 70);
  var last = StrategyUtils.LastOrDefaultSafe(ehma);

Migration Plan (Phase 2 – no behavior change)
1) Introduce `StrategyUtils` with methods above.
2) Update AroonStrategy, HullSMAStrategy, EnhancedMACDStrategy to:
   - Use CreateGet/ParseKlines
   - Use GetLastClosedPair for candle selection (read-only in Phase 2; enforcement moved to Phase 3)
   - Use CalculateEHMA for their current EHMA logic
3) Run build & minimal backtest smoke to confirm identical behavior (signal counts should not change in Phase 2).
4) Later phases:
  - Phase 3: Enforce closed-candle usage across all strategies (replace klines.Last() with policy-controlled selection). Add instrumentation logging (TraceSignalCandle) to audit before/after differences. Fix Aroon down-cross logic and ensure divergence strategies reference only finalized candles.
  - Phase 4: Externalize parameters (periods, thresholds, multipliers) into config (appsettings + injection) and add validation layer.
  - Phase 5: Test harness + unit tests for StrategyUtils and per-strategy deterministic backtest fixtures.
  - Phase 6: Performance (shared klines cache, reduced HTTP churn, async batching).
  - Phase 7: Advanced analytics (PnL attribution, slippage modeling, Monte Carlo scenario generation).

---
## Phase 3 Introduction: Closed-Candle Enforcement & Signal Integrity

Problem Today
Historically (pre-Phase 3) strategies evaluated indicators using the forming candle (latest `klines.Last()`) which can repaint intra-interval. This causes:
- Early entries that vanish if the candle reverses before close.
- Divergence detection instability (extrema not confirmed until close).
- MACD/RSI cross overshoot/undershoot noise.

Objectives
1. Uniform candle policy: Signals derive from last fully closed candle; comparisons use its predecessor.
2. Indicator input trimming: Indicator series exclude the forming candle when policy = Closed.
3. Auditable transition: Log every order with metadata (policy mode, evaluated candle close time vs. server now) for before/after diffs.
4. Zero silent behavioral drift: Transition gated behind a UI checkbox (no environment variable). Forming remains default; closed mode opt-in.

New Utilities Added in Code (Phase 2 tail → Phase 3 rollout)
- `StrategyRuntimeConfig.UseClosedCandles` — runtime flag controlled by desktop UI checkbox (persisted); default false.
- `StrategyBase.SupportsClosedCandles` (virtual) — strategy-level capability. Only strategies overriding this to `true` will honor closed mode.
- `StrategyBase.UseClosedCandles` (effective) — evaluates `StrategyRuntimeConfig.UseClosedCandles && SupportsClosedCandles`.
- Startup diagnostics — each strategy logs its effective policy and capability on construction.
- `StrategyUtils.ToIndicatorQuotes(klines, useClosedCandle)` — builds quote list trimming the forming candle when closed mode.
- `StrategyUtils.SelectSignalPair(klines, useClosedCandle)` — returns (signal, previous) pair abstracting index math.
- `StrategyUtils.ExcludeForming(klines)` — helper for manual operations when needed.

Upcoming Additions (Phase 3 tasks)
- `TraceSignalCandle(strategyName, symbol, mode, signalCloseTime, evaluatedPrice)` helper (lightweight, optional logger interface injection later).
- Remaining strategy refactors: any legacy strategies still directly using `klines.Last()` will migrate to `SelectSignalPair` & trimmed quotes.
- Divergence strategies: confirm pivot points only on closed candles; avoid counting incomplete swing lows/highs.
- Aroon: revisit down-cross logic (currently simplified) ensuring it doesn’t misfire mid-candle.

Risk Mitigation
- Incremental rollout: converted a pilot set first (MACDStandard, RSIMomentum, SupportResistance) then expanded.
- Fallback: simply leave UI checkbox unchecked (no code rollback required) for forming behavior.
- Validation metric: Count of signals per strategy (forming vs closed) and outcome delta (positions opened) archived.

Success Criteria
- All closed-capable strategies produce entries only at finalized candle boundaries when closed mode enabled.
- Unsupported strategies clearly log fallback to forming mode (predictable mixed behavior).
- No nullability or indexing regressions (helper centralization prevents off-by-one mistakes).
- Logged metadata shows consistent timing: signalCloseTime <= now - intervalDuration.

Non-Goals (Phase 3)
- No parameter optimization.
- No order sizing changes.
- No new indicator families.

Follow-Up (Post Phase 3)
- Update each strategy markdown: add “Candle Policy” section noting closed capability & prior forming behavior.
- Backtest differential analysis: run historical simulation with forming vs closed to quantify signal stability changes.
- UI enhancement (optional): annotate strategy list with a Closed-capable badge.

### Current Status (November 2025)
Most (but not all) strategies now override `SupportsClosedCandles = true` and therefore react to the checkbox:
- Converted: MACDStandard, MACDDivergence, RSIMomentum, SimpleSMA375, SMAExpansion, BollingerSqueeze, FibonacciRetracement, CandleDistributionReversal, SupportResistance, Aroon, HullSMA, EmaStochRsi, FVG, IchimokuCloud, EnhancedMACD.
- Pending/legacy (forming-only until refactored): any remaining strategies without the override (they will log capability = false and ignore the checkbox).

Operational Semantics:
- Checkbox OFF: All strategies run forming-candle mode (historical behavior) for consistency.
- Checkbox ON: Closed-capable strategies evaluate indicators on the last fully closed candle; forming-only strategies continue using the forming candle (mixed mode acceptable, clearly logged).

Startup Logging Example:
```
[StrategyInit] Strategy=MACDStandard SupportsClosedCandles=True UseClosedCandles=True (effective closed candle policy active)
[StrategyInit] Strategy=LegacyFoo SupportsClosedCandles=False UseClosedCandles=False (fallback forming; checkbox ignored)
```

This phased adoption prevents silent behavior drift while allowing immediate safety improvements where implemented.

---

Test Plan (helpers)
- ParseKlines
  - Happy path with complete JSON
  - Missing fields/short arrays
  - Null/empty content returns empty list
- EHMA
  - Known-sequence verification (unit test with small synthetic data)
  - Alignment length equals quotes length; first `length` items zeroed
- Candle selection
  - Lists with <2 items -> (null, null)
  - Exactly 2/3 items -> correct pair returned

Non-Goals (Phase 2)
- No changes to signal rules or thresholds
- No behavioral diffs in live vs historical logic
- No configuration rewiring

Open Questions
- Do we want a global logger inside helpers, or keep helpers pure and let strategies log? (Proposal: keep helpers pure.)
- Do we need a shared Kline/Quote cache now, or defer to Phase 7 performance work?
- Where to store Phase 3 signal audit logs (structured JSON file per day vs console)?
- Whether to unify divergence swing detection into a shared module (Phase 3 or defer to Phase 5 tests layer)?
