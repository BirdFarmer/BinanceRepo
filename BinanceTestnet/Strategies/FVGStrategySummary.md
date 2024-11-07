# FVG Strategy Summary

The **FVG (Fair Value Gap) Strategy** seeks to capitalize on price imbalances by detecting "Fair Value Gaps" (FVGs) likely to be filled as the price reverts to mean levels. This strategy assumes an active market with continuous data updates to identify and exploit FVG zones effectively.

## Core Concepts

- **Fair Value Gap (FVG)**: An FVG is a price gap within a sequence of three consecutive candles, often created when the market makes a sharp move. This strategy assumes these gaps are revisited, providing entry points for reversals or trend continuations.
  - **Bullish FVG**: Defined by three consecutive bullish candles, with the gap between the **high of the first candle** and the **low of the third candle**.
  - **Bearish FVG**: Defined by three consecutive bearish candles, with the gap between the **high of the third candle** and the **low of the first candle**.

## Strategy Workflow

1. **Detecting FVGs**:
   - Identifies gaps based on the high and low values of a sequence of three consecutive bullish or bearish candles.
   - Flags these zones as potential reversal or continuation areas, where prices may revisit to "fill" the gap.

2. **Entry Signals**:
   - **Long Entry**: Price revisits a bullish FVG (gap between the high of the first and the low of the third candle in three consecutive bullish candles).
   - **Short Entry**: Price revisits a bearish FVG (gap between the low of the first and the high of the third candle in three consecutive bearish candles).

3. **Exit Criteria**:
   - Trades are exited once the FVG is filled or at predefined profit/loss thresholds to manage risk.

## Technical Indicators

- **RSI (Relative Strength Index)**: Adds an extra layer of confirmation to avoid entering trades against overextended trends.
- **Volume Filter**: Prioritizes FVG signals on high-volume moves, filtering out low-volume setups that may indicate weak price action.

## Strategy Implementation Details

### `RunAsync(string symbol, string interval)`

1. **Data Fetching**: Requests recent price and volume data for the specified symbol and interval from the exchange.
2. **FVG Detection**: Analyzes price gaps based on historical candle data using the updated logic:
   - Bullish FVG: Between the high of the first candle and the low of the third in a sequence of three bullish candles.
   - Bearish FVG: Between the low of the first candle and the high of the third in a sequence of three bearish candles.
3. **Entry Conditions**:
   - **Long Signal**: Price revisits a bullish FVG zone, with RSI confirming oversold conditions.
   - **Short Signal**: Price revisits a bearish FVG zone, with RSI indicating overbought conditions.
4. **Order Execution**: Places limit or market orders based on the FVG proximity.

### `RunOnHistoricalDataAsync(IEnumerable<Kline> historicalData)`

1. **Simulated Trade Execution**: Runs through historical data to apply the updated FVG detection logic and simulate trades.
2. **Performance Evaluation**: Measures profitability and trade frequency to assess viability and tune parameters.

## Key Considerations

1. **Active Market Requirement**: This strategy is most effective in markets with consistent price data updates to detect and act on FVGs before price shifts.
2. **Coin Pair Selection**: Operates across a rotating list of the 80 most active coin pairs, split evenly between those with the highest trading volume and the largest price changes. The list is updated every 20 cycles (5-minute intervals) to keep the strategy focused on high-activity markets.
3. **Over-Triggering**: Due to the prevalence of FVGs in active markets, this strategy can trigger frequently, sometimes across multiple coin pairs. To address this:
   - Consider increasing FVG thresholds or minimum gap sizes.
   - Apply additional filters such as minimum volume, volatility bands, or a higher RSI sensitivity.

---

**Note**: This strategy’s profitability in backtesting may differ in live trading due to the high trigger frequency and market variability. Fine-tuning FVG detection thresholds, using more stringent filters, or limiting trade count per cycle could help mitigate excessive entries and improve performance.

---
