# Candle Distribution Reversal Strategy

## Overview
The `CandleDistributionReversalStrategy` is a trading strategy designed to identify potential market reversals based on the distribution of green (bullish) and red (bearish) candles over specific lookback periods. It generates **long** and **short** signals by analyzing the proportion of green and red candles in both long-term and short-term windows.

---

## Mode availability in this application

This strategy is available in Real Live Trading only. In the desktop UI it is disabled in Live Paper and Backtest, with a tooltip explaining:

"Real-only strategy (uses order book data). Switch to Live Real to enable."

While the core idea can be evaluated on historical OHLCV data, the implementation here relies on real-time market microstructure that we don’t simulate in Paper/Backtest.

---

## Strategy Logic

### Key Components
1. **Long-Term Trend Analysis**:
   - Analyzes the last `longTermLookback` candles (e.g., 100 candles).
   - If more than `greenThreshold`% (e.g., 62%) of the candles are green, it indicates a **strong uptrend**.
   - If more than `redThreshold`% (e.g., 62%) of the candles are red, it indicates a **strong downtrend**.

2. **Short-Term Exhaustion Signal**:
   - Analyzes the last `shortTermLookback` candles (e.g., 6 candles).
   - If the number of green and red candles is equal, it suggests a **loss of momentum**.

3. **Trade Signals**:
   - **Short Signal**: Generated in a strong uptrend when the short-term candles are balanced.
   - **Long Signal**: Generated in a strong downtrend when the short-term candles are balanced.

---

## Implementation Details

### Methods

#### 1. **`RunAsync`**
- Fetches klines (candlestick data) from the Binance API.
- Parses the klines data into a list of `Kline` objects.
- Uses the `IdentifySignal` method to determine if a trade signal should be generated.
- Places a **long** or **short** order using the `OrderManager` if a signal is identified.

#### 2. **`RunOnHistoricalDataAsync`**
- Processes historical klines data.
- Simulates trades on historical data using the same logic as `RunAsync`.

#### 3. **`IdentifySignal`**
- Analyzes the distribution of green and red candles over the long-term and short-term lookback periods.
- Returns:
  - `1` for a **long signal**.
  - `-1` for a **short signal**.
  - `0` for no signal.

#### 4. **`ParseKlines`**
- Parses the raw klines data from the Binance API into a list of `Kline` objects.

#### 5. **`LogTradeSignal`**
- Logs the details of each trade signal, including the symbol, price, and direction (long/short).

#### 6. **`HandleErrorResponse`**
- Handles errors from the Binance API response and logs them.

---

## Key Parameters
- **`longTermLookback`**: The number of candles to analyze for the long-term trend (e.g., 100).
- **`shortTermLookback`**: The number of candles to analyze for short-term exhaustion (e.g., 6).
- **`greenThreshold`**: The percentage of green candles required to indicate a strong uptrend (e.g., 62%).
- **`redThreshold`**: The percentage of red candles required to indicate a strong downtrend (e.g., 62%).

---

## Example Workflow
1. **Fetch Data**:
   - The strategy fetches the latest klines data from the Binance API.

2. **Analyze Trends**:
   - It counts the number of green and red candles in the long-term and short-term lookback periods.

3. **Generate Signals**:
   - If the conditions for a **long** or **short** signal are met, the strategy places an order.

4. **Log Signals**:
   - The details of each trade signal are logged for debugging and analysis.

---

## Benefits
1. **Trend Identification**:
   - The strategy identifies strong trends using candle distribution.

2. **Exhaustion Detection**:
   - It detects potential reversals by analyzing short-term candle balance.

3. **Flexibility**:
   - The lookback periods and thresholds can be adjusted to suit different markets and timeframes.

---

## Next Steps
1. **Backtest**:
   - Test the strategy on historical data to evaluate its performance.

2. **Optimize Parameters**:
   - Experiment with different values for `longTermLookback`, `shortTermLookback`, `greenThreshold`, and `redThreshold`.

3. **Add Risk Management**:
   - Incorporate stop loss and take profit levels to manage risk.

4. **Deploy**:
   - Deploy the strategy on live data with proper risk management.

---
