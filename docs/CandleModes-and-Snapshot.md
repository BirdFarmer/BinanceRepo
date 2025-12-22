# Candle Modes and Snapshot Runbook

Purpose: Brief guide for running the app with deterministic candle evaluation and understanding the `Forming` / `Closed` / `Align` modes, snapshot behavior, and how to verify alignment and snapshot usage.

Audience: Operators and developers validating LivePaper / LiveReal / Backtest runs.

---

## Quick Start

- UI: Pick `Operation Mode` (Backtest / LivePaper / LiveReal).
- Candle Mode: Choose `Forming`, `Closed`, or `Align` (Backtest forces `Closed`).
- Start: Click `Start`. The service will log the effective candle mode and alignment at session start.

---

## Candle Modes (short)

- **Forming**
  - Evaluate using the current forming candle (latest in-flight candle).
  - Trade timing: Earliest entries; may repaint while candle is forming.

- **Closed**
  - Evaluate using the most recent closed candle (deterministic).
  - Trade timing: Entry decision only after candle close (no repaint).

- **Align**
  - Wait for canonical timeframe boundary (e.g., :00, :05 for 5m), then evaluate using the closed candle that just completed.
  - Trade timing: Deterministic and aligned across symbols/strategies; preferred for multi-symbol deterministic runs.

---

## Snapshot-aware strategies

- **What it means**: The runner fetches one kline snapshot per symbol per cycle and passes it to strategies that implement `ISnapshotAwareStrategy`. Those strategies avoid re-fetching and evaluate the identical snapshot.
- **Benefits**: Fewer API calls, consistent data across strategies, deterministic behavior.
- **Status**: A few strategies are converted. Converting additional high-impact strategies increases determinism and lowers API usage.

---

## Logs to inspect

- **Startup / mode confirmation**
  - Example: `Candle Mode: UseClosedCandles=True, AlignToBoundary=True (effective _uiAlignToBoundary=True)` — confirms service sees the UI selection.

- **Boundary wait**
  - Example: `Boundary-alignment enabled: waiting 12.3s until 2025-12-16T11:10:00Z (+250ms buffer)` — runner will wait to the exact frame boundary.

- **Snapshot fetch**
  - Example: `Fetched snapshot for evaluationTimestamp=2025-12-16T11:10:00.0000000Z, symbols=50` — snapshot fetched and evaluation timestamp is on the boundary.

- **Cycle cadence**
  - Example: `Cycle finished in 18279ms; waiting 281720ms until next tick (frame=00:05:00)` — frame-based fixed cadence; includes work time + remaining wait.

---

## Troubleshooting / Common issues

- **Aligned selected but runner ran immediately**
  - Check: UI actually propagated setting to service. Look for `SetBoundaryAlignment called: align=True` and the startup log line above.
  - Reason: Start clicked before UI change propagated or Start read stale config; confirm radio selection was present before clicking Start.

- **Fetched snapshot timestamp not on canonical boundary**
  - Check: `evaluationTimestamp` seconds should be `00` and minute % timeframe == 0 for `Align`. If not, alignment wasn't applied.

- **Strategies still hitting API a lot**
  - Check which strategies are snapshot-aware. Non-converted strategies will still fetch their own klines.

- **Partial snapshot failures**
  - Symptom: fewer symbols in snapshot. Runner logs `symbols=N`. If many missing, use retry/backoff and check network/API limits.

---

## Best Practices

- For deterministic backtests and comparison: use `Align` in LiveReal/LivePaper and `Closed` for Backtest (Backtest UI hides candle mode).
- For earliest entry / exploratory runs: use `Forming` (accept repainting).
- Convert high-volume/critical strategies to `ISnapshotAwareStrategy` first.
- Fetch snapshot with bounded concurrency and retries to avoid API throttling.

---

## Quick verification steps

1. Select `Align` and `5m` timeframe; ensure `Aligned` radio is checked.
2. Start a LivePaper session; watch logs for:
   - `SetBoundaryAlignment called: align=True`
   - `Candle Mode: ... AlignToBoundary=True ...`
   - `Boundary-alignment enabled: waiting ... until {nextBoundary}`
   - `Fetched snapshot for evaluationTimestamp={boundaryTimestamp}, symbols={N}`
3. Confirm strategies run and trades (if any) reference that `evaluationTimestamp`.

---

## Short UI tooltip copy (recommended)

- Candle Mode label: "Choose how candles are evaluated: Forming (live), Closed (after close), or Align (wait to timeframe boundary)."
- Forming (radio): "Forming: evaluate the current in-flight candle. Earliest signals; may repaint."
- Closed (radio): "Closed: evaluate the most recent closed candle. Deterministic; no repaint."
- Align (radio): "Align: wait for timeframe boundary (e.g., :00/:05) then evaluate closed candle. Best for deterministic multi-symbol runs."
- Start button (tooltip): "Start trading with current settings. Service logs will show effective candle mode and alignment."
- Strategy selector (FVG selected): "FVG prefers Forming mode (requires the current forming candle for entries). Closed/Aligned are disabled to preserve intended behavior."

---

## Where this file lives

`docs/CandleModes-and-Snapshot.md` — include this in any release notes or operator runbook.

---

(End of runbook)
