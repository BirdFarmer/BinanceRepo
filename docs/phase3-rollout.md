# Phase 3 Rollout: Closed-Candle Policy and Signal Integrity

Date: 2025-11-09
Branch: `96-strategies-clean-up-and-standardize`
Owner: BirdFarmer / BinanceRepo

## Summary
Phase 3 introduces an optional closed-candle evaluation policy controlled by the Desktop UI checkbox. Most active strategies (see list below) honor the toggle via `SupportsClosedCandles = true` and the helpers `SelectSignalPair` and `ToIndicatorQuotes`. One previously listed strategy (`SMAExpansion`) is not currently exposed in the UI. This phase focuses on signal stability, auditability, and zero-surprise behavior during transition.

## Goals
- Safer entries by evaluating on the last fully closed candle when enabled.
- Consistent indicator inputs (exclude forming candle in closed mode).
- Clear, uniform signal logging for audits.
- Backwards-compatible default (forming) and predictable mixed-mode behavior.

## Non-Goals
- No parameter tuning or optimization.
- No order sizing or risk model changes.
- No new indicator families.

## User Controls
- Desktop UI: Checkbox “Use closed candles for signals”. Default OFF.
- Effective policy per strategy = UI flag AND `SupportsClosedCandles`.
- Startup logs show per-strategy capability and effective mode.

## Key Changes
- `StrategyBase`:
  - `SupportsClosedCandles` (virtual) and effective `UseClosedCandles = RuntimeFlag && Supports...`.
  - Startup diagnostics per strategy instance.
- `StrategyUtils`:
  - `ToIndicatorQuotes`, `SelectSignalPair`, `ExcludeForming` for policy-aware computations.
  - `TraceSignalCandle` unified logging helper; retains `SignalTracer` delegate.
- Strategies converted (honor checkbox):
  - MACDStandard, MACDDivergence, RSIMomentum, SimpleSMA375,
    BollingerSqueeze, FibonacciRetracement, CandleDistributionReversal, SupportResistance,
    Aroon, HullSMA, EmaStochRsi, FVG, IchimokuCloud, EnhancedMACD.
  Notes:
  - `SMAExpansion` was previously referenced but is not currently selectable in the UI; exclude from coverage tally until re-added.
  - SimpleSMA375 lacks a dedicated detail markdown file (to be added).
  Coverage: All selectable strategies honor the checkbox; there are no forming-only legacy strategies exposed in the UI.

## Logging & Audit
- Every order logs a single-line `[Signal]` with fields:
  - strategy, symbol, mode=CLOSED|FORMING, signalClose, prevClose, price, detail
- Optional delegate `SignalTracer` for structured sinks.

## Tests
- New test project `BinanceTestnet.UnitTests`:
  - SelectSignalPair and ToIndicatorQuotes behavior (forming vs closed).
  - ParseKlines happy/error paths, EHMA alignment, BucketOrders aggregation.

## Manual Verification (quick)
1) Forming vs Closed smoke (single symbol/timeframe):
   - OFF: run short session; capture signals.
   - ON: rerun; confirm signal timestamps shift to last-closed and console shows mode=CLOSED.
2) Mixed mode sanity:
   - Pick one closed-capable strategy and one legacy. With checkbox ON, confirm closed-capable switches while legacy remains forming.

## 24h Audit Plan
- Run two sessions over the same 24h window:
  - Session A: forming (checkbox OFF)
  - Session B: closed (checkbox ON)
- Collect:
  - Trades CSV (timestamp, side, price, strategy)
  - Metrics summary (count, win/loss, PnL if available)
- Deliver:
  - Diff of entry counts/timestamps; short narrative on stability changes.

## Revert / Safety
- To revert behavior: leave checkbox OFF. No code rollback required.
- Capability logs ensure awareness if any strategy ignores the toggle.

## Follow-ups
- Null-safety guard sweep on remaining legacy strategies.
- Optional: UI badge for closed-capable strategies (deferred by product choice).
- Tag a baseline after audit: `phase3-baseline`.

## Change Log (high-level)
- StrategyBase: added capability flag and effective policy, startup logs.
- StrategyUtils: added policy-aware helpers and TraceSignalCandle.
- Strategies: migrated across the suite (see list above); standardized signal selection.
- Docs: strategy-utils design updated; this Phase 3 doc added.
