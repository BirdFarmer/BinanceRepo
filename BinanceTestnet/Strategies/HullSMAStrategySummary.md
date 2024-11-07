# HullSMAStrategy Overview

The `HullSMAStrategy` class implements a trading strategy based on the Hull Moving Average (HMA) and the Relative Strength Index (RSI) indicators. It is designed for use with the Binance API, specifically for futures trading.

## Key Features

- **Hull Moving Averages**: 
  - Short Hull length set to **35**.
  - Long Hull length set to **100**.
  
- **RSI Filter**: 
  - Avoids trading when RSI is between **40 and 60** (considered a "boring" zone).

- **Trade Execution**:
  - Places long orders when the short HMA crosses above the long HMA.
  - Places short orders when the short HMA crosses below the long HMA.
  
- **Asynchronous Operation**:
  - Utilizes asynchronous methods to fetch market data and execute trades.

## Methods

### RunAsync

- **Parameters**:
  - `symbol`: The trading pair symbol (e.g., "BTCUSDT").
  - `interval`: The time interval for fetching klines (e.g., "1m").
  
- **Functionality**:
  - Fetches the latest klines from the Binance API.
  - Parses the kline data into `Quote` objects.
  - Calculates the RSI and checks if it's outside the "boring" zone.
  - Computes the short and long HMA values.
  - Determines if a trade should be executed based on HMA crossings.

### RunOnHistoricalDataAsync

- **Parameters**:
  - `historicalCandles`: A collection of historical kline data.
  
- **Functionality**:
  - Processes historical data to generate trade signals.
  - Applies the same RSI filter as in `RunAsync`.
  - Checks for HMA crossings and places trades accordingly.

### CalculateEHMA

- **Parameters**:
  - `quotes`: A list of `Quote` objects.
  - `length`: The length for the Hull Moving Average calculation.
  
- **Functionality**:
  - Calculates the Exponential Hull Moving Average (EHMA) based on the provided quotes.

### ParseKlines

- **Parameters**:
  - `content`: JSON string containing kline data.
  
- **Functionality**:
  - Deserializes the JSON content into a list of `Kline` objects.

### Logging and Error Handling

- **LogError**: Logs error messages to the console.
- **HandleErrorResponse**: Handles errors returned from the Binance API.

## Usage

To use the `HullSMAStrategy`, instantiate the class with the necessary parameters (`RestClient`, `apiKey`, `OrderManager`, `Wallet`) and call the `RunAsync` or `RunOnHistoricalDataAsync` methods with the desired parameters.

## Considerations

- Ensure that the API keys and necessary permissions are correctly configured for executing trades.
- Review the RSI thresholds and Hull lengths to adjust for different market conditions and improve performance.

