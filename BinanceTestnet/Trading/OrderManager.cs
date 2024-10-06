using Serilog;
using System;
using System.Collections.Concurrent;
using BinanceTestnet.Models;
using BinanceLive.Services;
using BinanceTestnet.Enums;
using RestSharp;
using Skender.Stock.Indicators;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;
using System.Globalization;

namespace BinanceTestnet.Trading
{
    public class OrderManager
    {
        // Fields grouped by functionality
        private Wallet _wallet;
        private readonly RestClient _client;
        private readonly ExcelWriter _excelWriter;
        private readonly SelectedTradeDirection _tradeDirection;
        private readonly SelectedTradingStrategy _tradingStrategy;
        
        // Trading parameters
        private decimal _leverage;
        private decimal _takeProfit;
        private decimal _tpIteration;
        private string _interval;
        private OperationMode _operationMode;
        
        // Trade statistics
        private readonly ConcurrentDictionary<int, Trade> _activeTrades = new ConcurrentDictionary<int, Trade>();
        private Dictionary<string, (decimal lotSize, int pricePrecision)> _lotSizeCache;// = new ConcurrentDictionary<string, (decimal lotSize, int pricePrecision)>();
        private int _nextTradeId = 1;
        private decimal noOfTrades = 0;
        private decimal profitOfClosed = 0;
        private decimal longs = 0;
        private decimal shorts = 0;
        private decimal _marginPerTrade = 0;

        public OrderManager(Wallet wallet, decimal leverage, ExcelWriter excelWriter, OperationMode operationMode,
                            string interval, string fileName, decimal takeProfit,
                            SelectedTradeDirection tradeDirection, SelectedTradingStrategy tradingStrategy,
                            RestClient client, decimal tpIteration, decimal margin)
        {
            _wallet = wallet;
            _leverage = leverage;
            _excelWriter = excelWriter;
            _operationMode = operationMode;
            _interval = interval;
            _takeProfit = takeProfit;
            _tpIteration = tpIteration;
            _tradeDirection = tradeDirection;
            _tradingStrategy = tradingStrategy;
            _client = client;
            _marginPerTrade = margin;
            _lotSizeCache = new Dictionary<string, (decimal, int)>();
            _excelWriter.Initialize(fileName);
            InitializeLotSizes().Wait(); // Awaiting the task for initialization
        }

        private async Task InitializeLotSizes()
        {
            try
            {
                var exchangeInfo = await GetExchangeInfoAsync();
                foreach (var symbolInfo in exchangeInfo.Symbols)
                {
                    var lotSizeFilter = symbolInfo.Filters.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
                    var priceFilter = symbolInfo.PricePrecision;//.FirstOrDefault(f => f.FilterType == "PRICE_FILTER");
                                        
                    if (lotSizeFilter != null && priceFilter != null)
                    {
                         string stepSizeString = lotSizeFilter.StepSize.Replace('.', ',');
                        // Store both lot size and price precision
                        decimal lotSize = decimal.Parse(lotSizeFilter.StepSize, CultureInfo.InvariantCulture);

                        // Store both lot size and price precision
                        _lotSizeCache[symbolInfo.Symbol] = (lotSize, priceFilter);
                    }
                    else
                    {
                        Log.Warning($"Lot size or price filter not found for symbol: {symbolInfo.Symbol}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error initializing lot sizes: {ex.Message}");
            }
        }

        private async Task<ExchangeInfo> GetExchangeInfoAsync()
        {
            var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);
            request.AddHeader("Accept", "application/json");

            var response = await _client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                throw new Exception($"Failed to get exchange info. Status Code: {response.StatusCode}, Error: {response.ErrorMessage}");
            }

            try
            {
                return JsonConvert.DeserializeObject<ExchangeInfo>(response.Content);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error deserializing exchange info: {ex.Message}, Response: {response.Content}");
            }
        }

        public (decimal lotSize, int pricePrecision) GetLotSizeAndPrecision(string symbol)
        {
            return _lotSizeCache.TryGetValue(symbol, out var info) ? info : (0, 0);
        }

        private decimal CalculateQuantity(decimal price, string symbol, decimal initialMargin)
        {
            decimal marginPerTrade = initialMargin;
            decimal quantity = (marginPerTrade * _leverage) / price;

            // Get the lot size and price precision for the symbol
            var (lotSize, pricePrecision) = GetLotSizeAndPrecision(symbol);
            if (lotSize > 0)
            {
                // Round the quantity down to the nearest lot size
                quantity = Math.Floor(quantity / lotSize) * lotSize;
            }
            else
            {
                Log.Warning($"Warning: Could not retrieve lot size for {symbol}. Using raw quantity.");
            }

            return quantity;
        }

        // Method to round price based on price precision
        private decimal RoundPrice(decimal price, int pricePrecision)
        {
            return Math.Round(price, pricePrecision);
        }

        public async Task PlaceLongOrderAsync(string symbol, decimal price, string signal, long timestamp, decimal? takeProfit = null)
        {
            await PlaceOrderAsync(symbol, price, true, signal, timestamp, takeProfit);
        }

        public async Task PlaceShortOrderAsync(string symbol, decimal price, string signal, long timestamp, decimal? takeProfit = null)
        {
            await PlaceOrderAsync(symbol, price, false, signal, timestamp, takeProfit);
        }

        private async Task PlaceOrderAsync(string symbol, decimal price, bool isLong, string signal, long timestampEntry, decimal? takeProfit = null)
        {
            lock (_activeTrades) // Lock to ensure thread safety
            {
                if ((isLong && _tradeDirection == SelectedTradeDirection.OnlyShorts) ||
                    (!isLong && _tradeDirection == SelectedTradeDirection.OnlyLongs))
                {
                    // Skipping trade due to trade direction preference
                    return;
                }

                if (_activeTrades.Count >= 2)
                {
                    // Skipping new trade as max active trades limit reached
                    return;
                }

                if (_activeTrades.Values.Any(t => t.Symbol == symbol && t.IsInTrade))
                {
                    // Skipping trade as trade for symbol is already active
                    return;
                }
            }

            decimal takeProfitPrice;
            decimal stopLossPrice;

            if (takeProfit.HasValue)
            {
                takeProfitPrice = takeProfit.Value;

                // Calculate risk (TP distance)
                decimal riskDistance = takeProfitPrice - price;

                // SL should be at half of the TP distance
                if (isLong)
                {
                    stopLossPrice = price - (riskDistance / 2); // SL below entry
                }
                else
                {
                    riskDistance = price - takeProfitPrice;
                    stopLossPrice = price + (riskDistance / 2); // SL above entry
                }
            }
            else
            {
                // If no TP provided, fall back to ATR-based TP and SL
                var (tpPercent, slPercent) = await CalculateATRBasedTPandSL(symbol);

                if (isLong)
                {
                    takeProfitPrice = price * (1 + (tpPercent / 100));
                    stopLossPrice = price * (1 - (slPercent / 100));
                }
                else
                {
                    takeProfitPrice = price * (1 - (tpPercent / 100));
                    stopLossPrice = price * (1 + (slPercent / 100));
                }
            }

            decimal quantity = CalculateQuantity(price, symbol, _marginPerTrade);

            var trade = new Trade(_nextTradeId++, symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage, signal, _interval, timestampEntry);

            // Check whether this is a paper trade or real trade
            if (_operationMode == OperationMode.LiveRealTrading) // Assuming you have an enum or property to track trading mode
            {
                await PlaceRealOrdersAsync(trade); // Place real order
            }
            else
            {
                trade.IsInTrade = true;
                // Paper trade - just continue with local trade placement logic
                lock (_activeTrades) // Lock to ensure thread safety
                {
                    if (_wallet.PlaceTrade(trade))
                    {
                        Console.WriteLine($"With signal: {signal}");
                        if (trade.IsLong) longs++;
                        else shorts++;
                        noOfTrades++;
                        _activeTrades[trade.Id] = trade;
                        _excelWriter.RewriteActiveTradesSheet(_activeTrades.Values.ToList(), _tpIteration);
                    }
                }
            }

            // Small delay to ensure `_activeTrades` is updated
            //await Task.Delay(100);
        }

        private async Task<(decimal tpPercent, decimal slPercent)> CalculateATRBasedTPandSL(string symbol)
        {
            // Fetch historical data for ATR calculation
            var symbolHistoryForATR = await DataFetchingUtility.FetchHistoricalData(_client, symbol, _interval);
            var btcHistoryForATR = await DataFetchingUtility.FetchHistoricalData(_client, "BTCUSDT", _interval);

            // Convert Kline data to Quote data for ATR calculation
            var symbolQuotes = symbolHistoryForATR.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();

            var btcQuotes = btcHistoryForATR.Select(k => new Skender.Stock.Indicators.Quote
            {
                Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                Open = k.Open,
                High = k.High,
                Low = k.Low,
                Close = k.Close,
                Volume = k.Volume
            }).ToList();

            //return VolatilityBasedTPandSL.CalculateTpAndSl(symbol, symbolQuotes, btcQuotes, _takeProfit);
            return VolatilityBasedTPandSL.CalculateTpAndSlBasedOnAtrMultiplier(symbol, symbolQuotes, _takeProfit);
        }

        public async Task CheckAndCloseTrades(Dictionary<string, decimal> currentPrices)
        {
            foreach (var trade in _activeTrades.Values.ToList())
            {
                if (currentPrices != null && trade != null &&
                    currentPrices.ContainsKey(trade.Symbol) &&
                    ShouldCloseTrade(trade, currentPrices[trade.Symbol]))
                {
                    var closingPrice = currentPrices[trade.Symbol];
                    trade.CloseTrade(closingPrice);

                    var profit = trade.IsLong
                        ? (trade.Quantity * (closingPrice - trade.EntryPrice)) + trade.InitialMargin
                        : (trade.Quantity * (trade.EntryPrice - closingPrice)) + trade.InitialMargin;

                    Console.WriteLine($"Trade for {trade.Symbol} closed.");
                    Console.WriteLine($"Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                    _wallet.AddFunds(profit);
                    profitOfClosed += profit;
                    _activeTrades.TryRemove(trade.Id, out _);

                    _excelWriter.WriteClosedTradeToExcel(trade, _takeProfit, _tpIteration, _activeTrades, _interval);
                    await Task.CompletedTask;
                }
            }
        }

        private bool ShouldCloseTrade(Trade trade, decimal currentPrice)
        {
            return trade.IsLong
                ? (currentPrice >= trade.TakeProfitPrice || currentPrice <= trade.StopLossPrice)
                : (currentPrice <= trade.TakeProfitPrice || currentPrice >= trade.StopLossPrice);
        }

        public void PrintActiveTrades(Dictionary<string, decimal> currentPrices)
        {
            Console.WriteLine("Active Trades:");
            string logOutput = "Active Trades:\n";

            var activeTradesSnapshot = _activeTrades.ToList();

            foreach (var tradePair in activeTradesSnapshot)
            {
                var trade = tradePair.Value;
                if (trade.Symbol != null && currentPrices.ContainsKey(trade.Symbol))
                {
                    decimal leverageMultiplier = trade.Leverage;
                    decimal initialValue = trade.InitialMargin * leverageMultiplier;
                    decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
                    decimal realizedReturn = ((currentValue - initialValue) / initialValue) * 100;

                    var direction = trade.IsLong ? "LONG" : "SHORT";
                    if (direction == "SHORT")
                    {
                        realizedReturn = ((initialValue - currentValue) / initialValue) * 100;
                    }
                    string entryTime = trade.EntryTimestamp.Hour + ":" + trade.EntryTimestamp.Minute;
                    
                    Console.WriteLine($"{trade.Symbol}: {direction}, Entry Price: {trade.EntryPrice:F5}, @ {entryTime}, Take Profit: {trade.TakeProfitPrice:F5}, Stop Loss: {trade.StopLossPrice:F5}, Leverage: {trade.Leverage:F1}, Current Price: {currentPrices[trade.Symbol]:F5}, Realized Return: {realizedReturn:F2}%");
                    logOutput += $"{trade.Symbol}: Direction: {direction}, Entry Price: {trade.EntryPrice:F5}, Current Price: {currentPrices[trade.Symbol]:F5}, Take Profit: {trade.TakeProfitPrice:F5}, Stop Loss: {trade.StopLossPrice:F5}, Leverage: {trade.Leverage:F1}, Realized Return: {realizedReturn:F2}%\n";
                }
            }

            Log.Information(logOutput);
        }

        public void PrintTradeSummary()
        {
            Log.Information($"No. of Trades: {noOfTrades}");
            Log.Information($"Longs: {longs}");
            Log.Information($"Shorts: {shorts}");
        }

        public void PrintWalletBalance()
        {
            Console.WriteLine($"Wallet Balance: {_wallet.GetBalance():F2} USDT");
        }

        public async Task RunBacktest(List<Kline> historicalData)
        {
            if (_operationMode != OperationMode.Backtest) return;

            var currentPrices = new Dictionary<string, decimal>();

            foreach (var kline in historicalData)
            {
                currentPrices["SYMBOL"] = kline.Close;

                await CheckAndCloseTrades(currentPrices);
            }

            PrintTradeSummary();
            PrintWalletBalance();
        }

        public void CloseAllActiveTrades(decimal closePrice)
        {
            foreach (var trade in _activeTrades.Values.ToList())
            {
                if(closePrice == -1)
                {
                    closePrice = trade.EntryPrice;
                }
                if (!trade.IsClosed)
                {
                    trade.CloseTrade(closePrice);
                    var profit = trade.IsLong
                        ? (trade.Quantity * (closePrice - trade.EntryPrice)) + trade.InitialMargin
                        : (trade.Quantity * (trade.EntryPrice - closePrice)) + trade.InitialMargin;

                    Console.WriteLine($"Trade for {trade.Symbol} closed.");
                    Console.WriteLine($"Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                    _wallet.AddFunds(profit);
                    profitOfClosed += profit;
                    _activeTrades.TryRemove(trade.Id, out _);

                    _excelWriter.WriteClosedTradeToExcel(trade, _takeProfit, _tpIteration, _activeTrades, _interval);
                }
            }
        }

        public void UpdateSettings(decimal leverage, string interval)
        {
            _leverage = leverage;
            _interval = interval;
        }

        public void UpdateParams(Wallet wallet, decimal tpPercent)
        {
            _takeProfit = tpPercent;
            _tpIteration = _takeProfit;
            _wallet = wallet;
        }

        public async Task PlaceRealOrdersAsync(Trade trade)
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");            

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
                throw new Exception("API Key or Secret Key is missing");

            if (trade != null && !await IsSymbolInOpenPositionAsync(trade.Symbol))
            {
                await SetLeverageAsync(trade.Symbol, trade.Leverage, "ISOLATED");

                var orderType = trade.IsLong ? "BUY" : "SELL";

                // Calculate the quantity based on the current price
                decimal price = trade.EntryPrice; // Assuming trade has an EntryPrice property
                decimal quantity = CalculateQuantity(price, trade.Symbol, trade.InitialMargin); // Calculate based on price and symbol

                long serverTime = await GetServerTimeAsync();

                // Step 1: Place the market order
                var orderRequest = new RestRequest("/fapi/v1/order", Method.Post);
                orderRequest.AddParameter("symbol", trade.Symbol);
                orderRequest.AddParameter("side", orderType);
                orderRequest.AddParameter("type", "MARKET"); // Market order
                orderRequest.AddParameter("quantity", quantity); // Use formatted quantity
                orderRequest.AddParameter("timestamp", serverTime);

                // Generate signature for the request
                var queryString = orderRequest.Parameters
                    .Where(p => p.Type == ParameterType.GetOrPost)
                    .Select(p => $"{p.Name}={p.Value}")
                    .Aggregate((current, next) => $"{current}&{next}");

                var signature = GenerateSignature(queryString);
                orderRequest.AddParameter("signature", signature);

                // Add API key to the request header
                orderRequest.AddHeader("X-MBX-APIKEY", apiKey);

                // Execute the request and place the market order
                var response = await _client.ExecuteAsync(orderRequest);

                if (response.IsSuccessful)
                {
                    var orderData = JsonConvert.DeserializeObject<OrderResponse>(response.Content);
                    var orderId = orderData.orderId; // Capture the order ID from the market order response
                    var qtyInTrade = orderData.executedQty;
                    Console.WriteLine($"{orderType} Order placed for {trade.Symbol} at {trade.EntryPrice}. Quantity: {qtyInTrade}, Order ID: {orderId}");

                    trade.IsInTrade = true; // Mark the trade as active in the system

                    // Regenerate timestamp for the next request
                    serverTime = await GetServerTimeAsync(5005);

                    // Get lot size precision for formatting the quantity
                    int lotSizePrecision = GetLotSizePrecision(trade.Symbol);
                    string formattedQuantity = quantity.ToString("F" + lotSizePrecision); // Format quantity correctly based on precision

                    var pricePrecision = _lotSizeCache[trade.Symbol].pricePrecision;
                    string stopLossPriceString = trade.StopLossPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);

                    // Step 2: Place Stop Loss as a separate STOP_MARKET order
                    var stopLossRequest = new RestRequest("/fapi/v1/order", Method.Post);
                    stopLossRequest.AddParameter("symbol", trade.Symbol);
                    stopLossRequest.AddParameter("side", trade.IsLong ? "SELL" : "BUY"); // Opposite direction for SL
                    stopLossRequest.AddParameter("type", "STOP_MARKET");
                    stopLossRequest.AddParameter("stopPrice", stopLossPriceString);
                    stopLossRequest.AddParameter("closePosition", "true"); // Close the entire position
                    stopLossRequest.AddParameter("newClientOrderId", orderId); // Referencing the market order
                    stopLossRequest.AddParameter("timestamp", serverTime);

                    // Generate signature for stop loss
                    var stopLossQueryString = stopLossRequest.Parameters
                        .Where(p2 => p2.Type == ParameterType.GetOrPost)
                        .Select(p2 => $"{p2.Name}={p2.Value}")
                        .Aggregate((current, next) => $"{current}&{next}");

                    Console.WriteLine("Query String for Signature: " + stopLossQueryString);
                    var stopLossSignature = GenerateSignature(stopLossQueryString);
                    stopLossRequest.AddParameter("signature", stopLossSignature);
                    stopLossRequest.AddHeader("X-MBX-APIKEY", apiKey);

                    var stopLossResponse = await _client.ExecuteAsync(stopLossRequest);

                    if (stopLossResponse.IsSuccessful)
                    {
                        Console.WriteLine($"Stop Loss set for {trade.Symbol} at {trade.StopLossPrice}");

                        orderData = JsonConvert.DeserializeObject<OrderResponse>(stopLossResponse.Content);
                        orderId = orderData.orderId; // Capture the order ID from the market order response
                    }
                    else
                    {
                        Console.WriteLine($"Failed to place Stop Loss for {trade.Symbol}. Error: {stopLossResponse.ErrorMessage}");
                        Console.WriteLine($"Response Status Code: {stopLossResponse.StatusCode}");
                        Console.WriteLine($"Response Content: {stopLossResponse.Content}");
                    }

                    // Regenerate timestamp for the next request
                    serverTime = await GetServerTimeAsync(5005);
                    
                    string takeProfitPriceString = trade.TakeProfitPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);

                    // Step 3: Place Take Profit Order separately
                    var takeProfitRequest = new RestRequest("/fapi/v1/order", Method.Post);
                    takeProfitRequest.AddParameter("symbol", trade.Symbol);
                    takeProfitRequest.AddParameter("side", trade.IsLong ? "SELL" : "BUY"); // Opposite direction for TP
                    takeProfitRequest.AddParameter("type", "TAKE_PROFIT_MARKET");
                    takeProfitRequest.AddParameter("stopPrice", takeProfitPriceString);
                    takeProfitRequest.AddParameter("closePosition", "true"); // Close the entire position
                    takeProfitRequest.AddParameter("newClientOrderId", orderId); // Referencing the market order
                    takeProfitRequest.AddParameter("timestamp", serverTime);

                    // Generate signature for take profit order
                    var takeProfitQueryString = takeProfitRequest.Parameters
                        .Where(p3 => p3.Type == ParameterType.GetOrPost)
                        .Select(p3 => $"{p3.Name}={p3.Value}")
                        .Aggregate((current, next) => $"{current}&{next}");

                    Console.WriteLine("Query String for Signature: " + takeProfitQueryString);
                    var takeProfitSignature = GenerateSignature(takeProfitQueryString);
                    takeProfitRequest.AddParameter("signature", takeProfitSignature);
                    takeProfitRequest.AddHeader("X-MBX-APIKEY", apiKey);

                    var takeProfitResponse = await _client.ExecuteAsync(takeProfitRequest);

                    if (takeProfitResponse.IsSuccessful)
                    {
                        Console.WriteLine($"Take Profit set for {trade.Symbol} at {trade.TakeProfitPrice}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to place Take Profit for {trade.Symbol}. Error: {takeProfitResponse.ErrorMessage}");
                        Console.WriteLine($"Response Content: {takeProfitResponse.Content}");
                    }
                }
                else
                {
                    // Enhanced debug information
                    Console.WriteLine($"Failed to place {orderType} Order for {trade.Symbol}. Error: {response.ErrorMessage}");
                    Console.WriteLine($"Response Status Code: {response.StatusCode}");
                    Console.WriteLine($"Response Content: {response.Content}"); // This should contain error details from Binance
                }
            }
        }
        
        private async Task<bool> IsSymbolInOpenPositionAsync(string symbol)
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");   

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // Step 1: Create query parameters
            var queryString = $"symbol={symbol}&timestamp={timestamp}";
            
            // Step 2: Generate signature for the request (assuming you have a generateSignature function)
            var signature = GenerateSignature(queryString);
            
            // Step 3: Add signature to the query string
            queryString += $"&signature={signature}";

            // Step 4: Make the API call
            var request = new RestRequest($"/fapi/v2/positionRisk?{queryString}", Method.Get);
            request.AddHeader("Accept", "application/json");
            // Add API key to the request header
            request.AddHeader("X-MBX-APIKEY", apiKey);

            var response = await _client.ExecuteAsync(request);
            if (!response.IsSuccessful)
            {
                Log.Error($"Failed to fetch open positions: {response.StatusCode}, {response.ErrorMessage}");
                return false;
            }

            try
            {
                var openPositions = JsonConvert.DeserializeObject<List<PositionRisk>>(response.Content);
                var openPosition = openPositions.FirstOrDefault(pos => pos.Symbol == symbol && pos.PositionAmt != 0);
                return openPosition != null;
            }
            catch (Exception ex)
            {
                Log.Error($"Error checking open position: {ex.Message}, Response: {response.Content}");
                return false;
            }
        }


        private int GetLotSizePrecision(string symbol)
        {
            if (_lotSizeCache.TryGetValue(symbol, out var lotSizeInfo)) // Use var to get the tuple
            {
                // Extract the lot size from the tuple
                decimal lotSize = lotSizeInfo.lotSize;

                // Calculate the precision based on the lot size
                return (int)(-Math.Log10((double)lotSize));
            }

            Console.WriteLine($"Lot size not found for {symbol}");
            return 0; // Default precision
        }

        private string GenerateSignature(string queryString)
        {
            string? apiSecret = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
            var keyBytes = Encoding.UTF8.GetBytes(apiSecret);
            var queryBytes = Encoding.UTF8.GetBytes(queryString);

            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(queryBytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public async Task SetLeverageAsync(string symbol, decimal leverage, string marginType = "ISOLATED")
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");

            long serverTime = await GetServerTimeAsync();
            
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
                throw new Exception("API Key or Secret Key is missing");

            var client = new RestClient("https://fapi.binance.com");

            // Step 1: Set Margin Type (ISOLATED or CROSS)
            var marginTypeRequest = new RestRequest("/fapi/v1/marginType", Method.Post);
            marginTypeRequest.AddParameter("symbol", symbol);
            marginTypeRequest.AddParameter("marginType", marginType); // Set to ISOLATED or CROSS
            marginTypeRequest.AddParameter("timestamp", serverTime);

            // Generate signature for the request
            var queryString = marginTypeRequest.Parameters
                                .Where(p => p.Type == ParameterType.GetOrPost)
                                .Select(p => $"{p.Name}={p.Value}")
                                .Aggregate((current, next) => $"{current}&{next}");
            
            var signature = GenerateSignature(queryString);
            marginTypeRequest.AddParameter("signature", signature);

            // Add API key to the request header
            marginTypeRequest.AddHeader("X-MBX-APIKEY", apiKey);


            // Log the request details
            Console.WriteLine("Setting margin type:");
            Console.WriteLine($"Symbol: {symbol}, Margin Type: {marginType}, Timestamp: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

            // Execute the request and log the response
            var marginTypeResponse = await client.ExecuteAsync(marginTypeRequest);
            Console.WriteLine("Response Content: " + marginTypeResponse.Content);

            if (!marginTypeResponse.IsSuccessful)
            {
                var errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(marginTypeResponse.Content);
                //throw new Exception($"Failed to set margin type: {errorResponse.msg} (Code: {errorResponse.code})");
            }

            // Step 2: Set Leverage
            var leverageRequest = new RestRequest("/fapi/v1/leverage", Method.Post);
            leverageRequest.AddParameter("symbol", symbol);
            leverageRequest.AddParameter("leverage", leverage);
            //leverageRequest.AddParameter("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            leverageRequest.AddParameter("timestamp", serverTime);

            // Generate signature for leverage request
            queryString = leverageRequest.Parameters
                                .Where(p => p.Type == ParameterType.GetOrPost)
                                .Select(p => $"{p.Name}={p.Value}")
                                .Aggregate((current, next) => $"{current}&{next}");
            
            signature = GenerateSignature(queryString);
            leverageRequest.AddParameter("signature", signature);

            // Add API key to the request header
            leverageRequest.AddHeader("X-MBX-APIKEY", apiKey);

            var leverageResponse = await client.ExecuteAsync(leverageRequest);

            if (!leverageResponse.IsSuccessful)
            {
                throw new Exception($"Failed to set leverage: {leverageResponse.Content}");
            }
        }

        public async Task CreateOCOOrderAsync(string symbol, decimal quantity, decimal takeProfitPrice, decimal stopLossPrice)
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");
            long serverTime = await GetServerTimeAsync();

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
                throw new Exception("API Key or Secret Key is missing");

            var client = new RestClient("https://fapi.binance.com");

            // Construct the OCO order request
            var ocoOrderRequest = new RestRequest("/fapi/v1/order/oco", Method.Post);
            ocoOrderRequest.AddParameter("symbol", symbol);
            ocoOrderRequest.AddParameter("side", quantity > 0 ? "SELL" : "BUY"); // Adjust according to your strategy
            ocoOrderRequest.AddParameter("quantity", quantity.ToString("F8")); // Ensure correct formatting
            ocoOrderRequest.AddParameter("price", takeProfitPrice.ToString("F8"));
            ocoOrderRequest.AddParameter("stopPrice", stopLossPrice.ToString("F8"));
            ocoOrderRequest.AddParameter("stopLimitPrice", stopLossPrice.ToString("F8")); // May want to adjust this based on strategy
            ocoOrderRequest.AddParameter("stopLimitTimeInForce", "GTC");
            
            // Add timestamp
            ocoOrderRequest.AddParameter("timestamp", serverTime);

            // Generate signature for the request
            var queryString = ocoOrderRequest.Parameters
                                .Where(p => p.Type == ParameterType.GetOrPost)
                                .Select(p => $"{p.Name}={p.Value}")
                                .Aggregate((current, next) => $"{current}&{next}");

            var signature = GenerateSignature(queryString);
            ocoOrderRequest.AddParameter("signature", signature);

            // Add API key to the request header
            ocoOrderRequest.AddHeader("X-MBX-APIKEY", apiKey);

            // Execute the request
            var response = await client.ExecuteAsync(ocoOrderRequest);

            if (response.IsSuccessful)
            {
                Console.WriteLine($"OCO order placed for {symbol} with TP at {takeProfitPrice} and SL at {stopLossPrice}.");
            }
            else
            {
                Console.WriteLine($"Failed to place OCO order for {symbol}. Error: {response.ErrorMessage}");
            }
        }
        
        public async Task<long> GetServerTimeAsync(int delayMilliseconds = 0)
        {
            // Introduce a delay if specified
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds);
            }

            var request = new RestRequest("/fapi/v1/time", Method.Get);
            var response = await _client.ExecuteAsync(request);
            if (response.IsSuccessful)
            {
                var serverTimeData = JsonConvert.DeserializeObject<ServerTimeResponse>(response.Content);
                return serverTimeData.ServerTime;
            }
            else
            {
                throw new Exception("Failed to fetch server time.");
            }
        }

        public class ServerTimeResponse
        {
            [JsonProperty("serverTime")]
            public long ServerTime { get; set; }
        }

        public class ErrorResponse
        {
            public int code { get; set; }
            public string msg { get; set; }
        }

        public class OrderResponse
        {
            public string symbol { get; set; }
            public long orderId { get; set; }
            public string clientOrderId { get; set; }
            public decimal price { get; set; }
            public decimal origQty { get; set; }
            public decimal executedQty { get; set; }
            public decimal cumQty { get; set; }
            public decimal cumQuote { get; set; }
            public string status { get; set; }
            public string timeInForce { get; set; }
            public string type { get; set; }
            public string side { get; set; }
            public decimal stopPrice { get; set; }
            public decimal icebergQty { get; set; }
            public long time { get; set; }
            public long updateTime { get; set; }
            public bool isWorking { get; set; }
            public decimal origQuoteOrderQty { get; set; }
        }

        public class PositionRisk
        {
            public string Symbol { get; set; }              // The symbol of the asset, e.g., "ADAUSDT"
            public string PositionSide { get; set; }        // "BOTH" for one-way mode
            public decimal PositionAmt { get; set; }        // Amount of the position
            public decimal EntryPrice { get; set; }         // The price at which the position was entered
            public decimal BreakEvenPrice { get; set; }     // Price at which position breaks even
            public decimal MarkPrice { get; set; }          // Current market price
            public decimal UnRealizedProfit { get; set; }   // Unrealized profit/loss for the position
            public decimal LiquidationPrice { get; set; }   // Liquidation price (if applicable)
            public decimal InitialMargin { get; set; }      // Initial margin required
            public decimal MaintMargin { get; set; }        // Maintenance margin required
            public long UpdateTime { get; set; }            // Time of last update (as a timestamp)
            
            // Additional fields that aren't directly present in the Binance response, 
            // but might be useful for your system.
            public decimal StopLoss { get; set; }           // Stop loss price
            public decimal TakeProfit { get; set; }         // Take profit price
            public bool IsTrailingStopActive { get; set; }  // Whether trailing stop is active
        }
    }
}