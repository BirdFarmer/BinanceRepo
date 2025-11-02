# Exit Management: Trailing Stop, Take Profit, and Stop Loss

This document explains how exits are configured and executed across all modes (Live Real, Live Paper, Backtest), including formulas, UI controls, Binance constraints, and the simulation algorithm.

## Overview

- Two exit modes are available (chosen in the desktop UI):
  - Take Profit: places a static TP and a static SL.
  - Trailing Stop: places a static SL plus a trailing stop order (for live) or simulates a trailing exit (paper/backtest). The trailing replaces the TP.
- Stop Loss is always present in both modes.

## UI controls (TradingAppDesktop)

- Exit Mode (ComboBox): Take Profit | Trailing Stop
- Shared parameter slider:
  - TP mode: ATR Multiplier for volatility-based TP/SL.
  - Trailing mode: Activation % (distance from entry to “arm” the trailing stop).
- Callback % slider (visible only in Trailing mode): the retrace percentage used once trailing is activated.
- Risk-Reward Ratio slider: used in trailing mode to derive SL distance from Activation %, matching your expected RR behavior.

Where to see them:
- File: `TradingAppDesktop/MainWindow.xaml`
- Code-behind toggling/flow: `TradingAppDesktop/MainWindow.xaml.cs`
- Service that consumes UI: `TradingAppDesktop/Services/BinanceTradingService.cs`

## Configuration wiring

- UI calls `BinanceTradingService.SetTrailingUiConfig(useTrailing, activationPercent, callbackPercent)` before starting a session.
- During `StartTrading(...)`, the service applies the config to the `OrderManager` via `UpdateTrailingConfig`.
- Core logic lives in `BinanceTestnet/Trading/OrderManager.cs`.

## Live Real Trading behavior

File: `BinanceTestnet/Trading/OrderManager.cs`

1) Place market entry (MARKET)
2) Place Stop Loss (STOP_MARKET, `closePosition=true`)
3) Depending on Exit Mode:
   - TP mode: place Take Profit (LIMIT)
   - Trailing mode: place Trailing Stop (TRAILING_STOP_MARKET) and skip TP

Details for trailing (live):
- Side: opposite of entry (SELL for long, BUY for short)
- Reduce only: `reduceOnly=true`
- Activation price (direction-aware):
  - Long: `activationPrice = entry * (1 + actPct/100)`
  - Short: `activationPrice = entry * (1 - actPct/100)`
- Callback rate clamped to Binance bounds: `[0.1, 5.0]` percent; formatted with one decimal.
- Precision/rounding: prices formatted with symbol-specific precision and/or tick size as per exchange info.

Requests use `POST /fapi/v1/order` with appropriate parameters.

## Paper + Backtest simulation behavior

When Trailing mode is enabled, TP is ignored and a trailing exit is simulated locally in `CheckAndCloseTrades(...)`:

- Activation threshold (from entry):
  - Long: if price >= activationPrice then trailing is “armed”.
  - Short: if price <= activationPrice then trailing is “armed”.
- After activation, track the extreme:
  - Long: highest price (peak) since activation.
  - Short: lowest price (trough) since activation.
- Exit condition: when the price retraces by Callback % from the tracked extreme:
  - Long retrace trigger: `peak * (1 - cbPct/100)`
  - Short retrace trigger: `trough * (1 + cbPct/100)`
- Stop Loss is always honored and may trigger before activation or before a trailing retrace.
- TP is not used when Trailing is enabled.

Per-trade trailing state used for simulation is stored in `Trade`:
- `TrailingEnabled`, `TrailingActivated`
- `TrailingActivationPercent`, `TrailingCallbackPercent`
- `TrailingActivationPrice`, `TrailingExtreme`

Files:
- `BinanceTestnet/Trading/OrderManager.cs` (simulation in `CheckAndCloseTrades`)
- `BinanceTestnet/Trading/Trade.cs` (per-trade trailing state)

## Stop Loss and Risk-Reward in Trailing mode

When Trailing is ON, the SL is derived from Activation % and the Risk-Reward divider (RR) to mirror your expected behavior:

- `slDistance = (activation% × entryPrice) / RR`
- Long SL: `entry - slDistance`
- Short SL: `entry + slDistance`

In TP mode (non-trailing), TP/SL are derived from an ATR-based model using the chosen ATR multiplier.

## Exchange constraints and rounding

- Exchange info (lot sizes, price precision, tick sizes) is fetched and cached at startup.
- Quantities are rounded down to the nearest lot size.
- Prices are formatted using the symbol’s precision; TP prices are floored to the nearest tick.
- In live mode, `reduceOnly=true` is used for trailing to ensure it only reduces/ closes the position.

## Liquidation safety adjustment

Before placing SL, a liquidation estimate is computed and the SL is adjusted away from liquidation with a small buffer if needed:
- Long liquidation estimate: `entry * (1 - 1/leverage + maintenanceRate)`
- Short liquidation estimate: `entry * (1 + 1/leverage - maintenanceRate)`
- If SL would violate this, it is nudged on the safe side by a small buffer (0.1% of entry).

## Files and key methods

- Desktop UI:
  - `TradingAppDesktop/MainWindow.xaml` (controls)
  - `TradingAppDesktop/MainWindow.xaml.cs` (visibility toggling, passing UI config)
  - `TradingAppDesktop/Services/BinanceTradingService.cs`:
    - `StartTrading(...)` applies `_orderManager.UpdateTrailingConfig(...)`
    - Implements `IExchangeInfoProvider` to feed OrderManager symbol metadata
- Engine:
  - `BinanceTestnet/Trading/OrderManager.cs`:
    - `UpdateTrailingConfig(...)`: enables trailing and stores activation/callback overrides
    - `PlaceOrderAsync(...)`: computes TP/SL and, in trailing mode, derives SL from Activation % and RR
    - `PlaceRealOrdersAsync(...)`: places MARKET, SL (STOP_MARKET), and either TP (LIMIT) or trailing (TRAILING_STOP_MARKET)
    - `PlaceTrailingStopLossAsync(...)`: computes activation price and clamps/ formats callback
    - `CheckAndCloseTrades(...)`: paper/backtest trailing simulation
  - `BinanceTestnet/Trading/Trade.cs`: includes trailing state for simulation

## Parameter summary

- Exit Mode:
  - Take Profit: ATR Multiplier applies for TP/SL.
  - Trailing Stop: uses Activation % and Callback %, and replaces TP with trailing.
- Activation % (Trailing): distance from entry to arm trailing; also used with RR to set SL distance in trailing mode.
- Callback % (Trailing): retrace from the post-activation extreme that triggers exit; clamped to [0.1, 5.0].
- Risk-Reward (Trailing): divider used to convert Activation % distance to SL distance.
- ATR Multiplier (TP): determines TP/SL percentages in non-trailing mode.

## Worked examples

Long (live or simulated):
- Entry = 100.00, Activation % = 1.0, Callback % = 1.0, RR = 3
- Activation price = 101.00
- SL distance = (1.0% × 100) / 3 = 0.3333 → SL = 99.6667
- After activation, if price peaks at 102.50, the trailing exit triggers at 102.50 × (1 − 0.01) = 101.475

Short (live or simulated):
- Entry = 200.00, Activation % = 0.5, Callback % = 0.8, RR = 2
- Activation price = 199.00
- SL distance = (0.5% × 200) / 2 = 0.5 → SL = 200.50
- After activation, if price troughs at 195.00, the trailing exit triggers at 195.00 × (1 + 0.008) = 196.56

## Edge cases and safeguards

- Callback below 0.1% or above 5.0% is auto-clamped.
- Trailing only activates once price crosses the activation threshold; before that, TP is not used when trailing mode is on.
- SL always takes precedence (may trigger before activation or trailing retrace).
- Duplicate live entries for the same symbol are prevented by checking open position risk.

## How to use

- In the desktop app:
  - Choose Exit Mode
  - Set ATR Multiplier (TP) or Activation % (Trailing)
  - If Trailing, set Callback %
  - Set Risk-Reward (used in trailing-mode SL calculation)
  - Start a Live, Paper, or Backtest session

- Programmatically:
  - Call `SetTrailingUiConfig(useTrailing, activationPct, callbackPct)` on `BinanceTradingService` before `StartTrading`.
  - Or call `UpdateTrailingConfig` directly on `OrderManager` if you construct it yourself.

## Testing notes

- Paper mode provides a fast way to validate trailing activation and exits by watching log lines:
  - “trailing activated” shows activation price and callback
  - “closing at trailing retrace” shows the extreme and retrace exit
- Backtest mode executes the same simulation over historical data.

## Future enhancements (optional)

- Option to keep a static TP active until trailing activation, then cancel TP.
- Persist UI exit settings between sessions.
- Add unit tests that feed synthetic price series to assert activation and retrace behavior.
