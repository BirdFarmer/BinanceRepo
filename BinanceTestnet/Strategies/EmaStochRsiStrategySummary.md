# EMA-StochRSI Strategy

## Overview
The `EmaStochRsiStrategy` is a C# trading strategy designed for the Binance cryptocurrency market. It utilizes Exponential Moving Averages (EMAs) and the Stochastic RSI to generate long and short trade signals based on trend and momentum conditions.

## Components and Dependencies
- **Indicators**:
  - **EMA (8, 14, 50)**: Used to determine market trend and entry conditions.
  - **Stochastic RSI (14, 3, 3)**: Confirms overbought and oversold conditions to refine entry signals.
- **Dependencies**:
  - `BinanceTestnet.Models` for market data structures.
  - `RestSharp` for REST API interactions.
  - `Skender.Stock.Indicators` for technical indicators.
  - `OrderManager` and `Wallet` classes for order and portfolio management.

## Signal Conditions
1. **Long Entry**:
   - Price is above EMA8, which is above EMA14, which is above EMA50.
   - Stochastic RSI crosses above its signal line from below.
2. **Short Entry**:
   - Price is below EMA8, which is below EMA14, which is below EMA50.
   - Stochastic RSI crosses below its signal line from above.

## Methods
- **RunAsync**: Executes the strategy on live data, fetching recent price data and checking for entry signals based on EMA and Stochastic RSI conditions.
- **RunOnHistoricalDataAsync**: Backtests the strategy on historical data, iterating through historical price action to identify potential signals.
- **ParseKlines**: Parses JSON candlestick data into structured `Kline` objects.
- **CreateRequest**: Configures the API request to fetch candlestick data.
- **LogTradeSignal**: Logs the direction of each trade (currently commented out).
- **HandleErrorResponse**: Logs any errors received from the API response.

## Error Handling
The strategy captures and logs exceptions from API requests and JSON parsing errors, along with any unsuccessful responses.

