# Multi backtest harness (phase 1) — strategy as-is

Labels: enhancement, research, backtesting, strategy, phase-1

## Summary

Build a structured “multi tester” harness to batch-run backtests using any selected strategy as-is (no entry-logic tweaks), across timeframes, symbol sets, and exit styles. The goal is to discover where each strategy thrives (coins, baskets, timeframes) and produce clear, comparable metrics (Expectancy, Payoff, WinRate, etc.).

## Scope (agreed for phase 1)

- Strategy remains as-is: no entry-logic tweaks; use current defaults (e.g., filters, candle policy) as configured in the selected strategy.
- Iterate these dimensions:
  - Timeframes: 5m, 15m, 1h
  - Symbol sets:
    - Majors: [BTCUSDT, ETHUSDT]
    - Momentum: [SOLUSDT, AVAXUSDT, LINKUSDT, INJUSDT, NEARUSDT]
    - Volatile microcaps (stress): [1000PEPEUSDT, DOGEUSDT, SHIBUSDT]
    - Mixed baskets: configurable lists
    - Large universe sampling (optional): pull a top-100 list and test ranked slices (e.g., 5–10, 50–55, 90–95)
  - Exit styles:
    - Fixed TP/SL (risk profiles below)
    - Trailing replacement (1–2 pre-defined presets)
  - Risk profiles (for fixed exits): 1:0.5, 1:1, 1.5:1, 3:1 (optional: 0.5:1)

Out of scope for phase 1 (revisit after analysis):
- Entry-parameter variations (e.g., indicator period changes)
- Enabling/disabling strategy-specific filters
- Alternative entry confirmations (e.g., momentum/acceleration flags)

## Week regime sampling (phase 1.5)

After the first sweep, re-run best presets across three distinct weeks:
- One bullish week
- One bearish week
- One ranging week

Goal: check robustness outside the most recent regime.

## Minimal configuration schema (YAML)

```yaml
strategy: StrategyName   # global default strategy for this batch (e.g., MACDStandard, RSIReversal)
timeframes: [5m, 15m, 1h]
symbolSets:
  # Ranked slices (approximate tiers as of current date)
  tier_1_5: [BTCUSDT, ETHUSDT, BNBUSDT, SOLUSDT, XRPUSDT]
  tier_6_10: [ADAUSDT, DOGEUSDT, TRXUSDT, TONUSDT, AVAXUSDT]
  tier_50_54: [SANDUSDT, THETAUSDT, AAVEUSDT, FTMUSDT, MANAUSDT]
  tier_90_94: [GALAUSDT, BLURUSDT, JASMYUSDT, SXPUSDT, MASKUSDT]
  # optional: ranked slices once a 100-coin universe is available
exitModes:
  - name: fixed
    trailing: false
    riskProfiles:
      - { name: rr_1_to_0_5, tpMultiplier: 0.5, slMultiplier: 1.0 }
      - { name: rr_1_to_1, tpMultiplier: 1.0, slMultiplier: 1.0 }
      - { name: rr_1_5_to_1, tpMultiplier: 1.5, slMultiplier: 1.0 }
      - { name: rr_3_to_1, tpMultiplier: 3.0, slMultiplier: 1.0 }
  - name: trailing_default
    trailing: true
    activationPct: 0.8   # ~0.8 x ATR% starting point
    callbackPct: 0.35    # ~0.35 x ATR%
output:
  directory: results/multi
  format: csv
```

### Selecting a strategy per run

- Global (simple): set `strategy: <StrategyName>` at the top of the YAML to apply one strategy to all combinations.
- Per-run (advanced, phase 2): support a `runs:` array where each item declares its own `strategy`, `timeframes`, `symbolSets`, and `exitModes` to mix multiple strategies in one batch.

Example (advanced):

```yaml
runs:
  - strategy: MACDStandard
    timeframes: [15m]
    symbolSets:
      momentum: [SOLUSDT, AVAXUSDT, LINKUSDT, INJUSDT, NEARUSDT]
    exitModes:
      - name: trailing_default
        trailing: true
        activationPct: 0.8
        callbackPct: 0.35
  - strategy: RSIReversal
    timeframes: [1h]
    symbolSets:
      majors: [BTCUSDT, ETHUSDT]
    exitModes:
      - name: fixed
        trailing: false
        riskProfiles:
          - { name: rr_1_to_1, tpMultiplier: 1.0, slMultiplier: 1.0 }
```

CLI override (optional): allow `--strategy <StrategyName>` to override the YAML for quick one-offs.

CSV columns per run:

```
sessionId,timeframe,symbolSet,exitMode,tpMult,slMult,trailing,activationPct,callbackPct,
trades,winRate,netPnl,avgWin,avgLoss,payoff,expectancy,maxConsecLoss,avgDuration,topSymbols,bottomSymbols
```

## Tasks

- [ ] Approve scope and dimensions for phase 1
- [ ] Add config file for combinations (YAML)
- [ ] Implement MultiBacktestRunner (orchestrator)
  - [ ] Pre-fetch/cache historical data per symbol/timeframe
  - [ ] Loop combinations; set sessionId per run
  - [ ] Apply exit mode and risk profile (without changing entry logic)
  - [ ] Run historical backtest using existing runner
  - [ ] Compute metrics and append CSV row
- [ ] Generate initial matrix:
  - [ ] Timeframes: 5m, 15m, 1h
  - [ ] Symbol sets: majors vs momentum vs microcaps vs mixed
  - [ ] Exit: fixed (1:1, 1.5:1, 3:1) vs trailing_default
- [ ] Aggregate & analyze: rank by Expectancy and Payoff; identify symbol pruning candidates
- [ ] Draft findings (top presets per timeframe + basket)
- [ ] Select three distinct weeks (bullish/bearish/ranging) and re-run top presets
- [ ] Finalize recommendations for production defaults

## Acceptance criteria

- Strategy code remains unchanged for entries in phase 1
- Runner executes all configured combinations and completes without blocking
- Output CSV produced with columns above; one row per run
- Historical data is cached per symbol/timeframe to avoid redundant downloads
- A markdown summary highlights:
  - Top presets by timeframe/basket
  - Symbols to prune (negative expectancy)
  - Fixed vs trailing comparison

## Nice to have

- Optional JSON per-run summary artifacts
- Simple HTML dashboard or notebook to pivot CSV results
- Hooks to re-use the same harness for the 3-week regime validation

---

Notes:
- Entry tweaks (e.g., zero-line/momentum/HTF alignment or equivalent per strategy) are postponed until after matrix analysis of the strategy as-is.
- Large-universe sampling (top-100 with ranked slices) can piggy-back on the same harness once a list endpoint is defined.
