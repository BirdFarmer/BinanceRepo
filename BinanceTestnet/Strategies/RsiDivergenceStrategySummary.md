# RSI Divergence Strategy Summary

## Strategy Logic
The RSI Divergence Strategy seeks to identify and act on bullish or bearish divergences in the market. It combines the following indicators:
1. **Relative Strength Index (RSI)** - Measures the strength of price movements, using a period of **20** for smoother trend identification.
2. **Stochastic Oscillator** - Used to confirm overbought and oversold conditions. The strategy takes action when the Stochastic dips below 10 (oversold) or rises above 90 (overbought).

### Entry Criteria:
- **Bullish Divergence (Long Entry)**:
  - RSI shows a bullish divergence, with the latest RSI higher than a previous low but the price making a new low.
  - Stochastic Oscillator (K) <= 10 to confirm oversold conditions.

- **Bearish Divergence (Short Entry)**:
  - RSI shows a bearish divergence, with the latest RSI lower than a previous high but the price making a new high.
  - Stochastic Oscillator (K) >= 90 to confirm overbought conditions.

### Exit Criteria:
- Trades are closed based on a separate mechanism managed by `OrderManager`, which checks current prices against open trade criteria.

### Special Condition:
- If the **Stochastic Oscillator** dips below **20** or rises above **80** in the opposite direction, the strategy will reset the lookback period. 
  - A dip below 20 will reset the "highest" RSI and price data for bearish signals.
  - A rise above 80 will reset the "lowest" RSI and price data for bullish signals.

## Optimal Market Conditions
- **Sideways or Range-bound Markets**: Works well when prices are oscillating within a range, allowing for consistent divergences.
- **Markets with Mild Trends**: Mild uptrends or downtrends can allow the RSI and Stochastic signals to reliably detect divergence without extreme volatility.

## Limitations
- **Strong Trends**: In highly trending markets, this strategy may lead to premature entries as divergences are less likely to indicate reversals.
- **High Volatility**: Sudden price spikes can make RSI and Stochastic levels fluctuate, creating misleading signals.

**Note**: Consider parameterizing RSI length or Stochastic thresholds to adapt to different market conditions.
