# Enhanced MACD Strategy

## Overview
The `EnhancedMACDStrategy` is a C# implementation of a trading strategy that combines the MACD indicator with RSI, Bollinger Bands, and EMAs to signal long or short trades on cryptocurrency markets. This strategy fetches candlestick data, calculates indicators, and identifies signals based on crossovers and other conditions.

## Components and Dependencies
- **Indicators**:
  - **MACD (12, 26, 9)**: Used to detect momentum and signal changes.
  - **RSI (14)**: Assesses overbought/oversold conditions.
  - **Bollinger Bands (20, 2)**: Helps determine price volatility.
  - **EMA (5 and 20)**: Used as a trend filter.
- **Dependencies**:
  - `BinanceTestnet.Models` for market data structures.
  - `RestSharp` for REST API interactions.
  - `Skender.Stock.Indicators` for technical indicators.
  - `OrderManager` and `Wallet` classes for trade management.

## Signal Conditions
1. **Long Entry**:
   - MACD line crosses above Signal line.
   - Short EMA (5) is above Long EMA (20).
2. **Short Entry**:
   - MACD line crosses below Signal line.
   - Short EMA (5) is below Long EMA (20).

## Methods
- **RunAsync**: Runs the strategy on live data, fetching candlestick data from Binance, calculating indicators, and placing trades based on signals.
- **RunOnHistoricalDataAsync**: Backtests the strategy on historical data.
- **ParseKlines**: Parses candlestick data from JSON response.
- **CreateRequest**: Configures the API request for fetching candlestick data.
- **LogTradeSignal**: Logs trade signals (currently commented out).
- **HandleErrorResponse**: Handles and logs API response errors.

## Error Handling
The strategy catches exceptions during API calls and JSON parsing errors, logging relevant error messages.

---

### Example Usage
```csharp
// var strategy = new EnhancedMACDStrategy(client, apiKey, orderManager, wallet);
// await strategy.RunAsync("BTCUSDT", "15m");
