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
using BinanceTestnet.Database;

namespace BinanceTestnet.Trading
{
    public class OrderManager
    {
        // Fields grouped by functionality
        private Wallet _wallet;
        private readonly DatabaseManager _databaseManager;
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
        private Dictionary<string, (decimal lotSize, int pricePrecision, decimal tickSize)> _lotSizeCache = new Dictionary<string, (decimal, int, decimal)>();

        private int _nextTradeId = 1;
        private decimal noOfTrades = 0;
        private decimal profitOfClosed = 0;
        private decimal longs = 0;
        private decimal shorts = 0;
        private decimal _marginPerTrade = 0;
        public DatabaseManager DatabaseManager => _databaseManager;

        public OrderManager(Wallet wallet, decimal leverage, ExcelWriter excelWriter, OperationMode operationMode,
                            string interval, string fileName, decimal takeProfit,
                            SelectedTradeDirection tradeDirection, SelectedTradingStrategy tradingStrategy,
                            RestClient client, decimal tpIteration, decimal margin, string databasePath)
        {
            _wallet = wallet;
            _databaseManager = new DatabaseManager(databasePath); // Create a new instance here
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
            _lotSizeCache = new Dictionary<string, (decimal, int, decimal)>();
            DatabaseManager.InitializeDatabase();
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
                    var priceFilter = symbolInfo.Filters.FirstOrDefault(f => f.FilterType == "PRICE_FILTER"); // Get PRICE_FILTER for tick size

                    if (lotSizeFilter != null && priceFilter != null)
                    {
                        // Parse lot size and tick size
                        decimal lotSize = decimal.Parse(lotSizeFilter.StepSize, CultureInfo.InvariantCulture);
                        decimal tickSize = decimal.Parse(priceFilter.TickSize, CultureInfo.InvariantCulture);

                        // Store lot size, price precision, and tick size in the cache
                        _lotSizeCache[symbolInfo.Symbol] = (lotSize, symbolInfo.PricePrecision, tickSize);
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

        public (decimal lotSize, int pricePrecision, decimal tickSize) GetLotSizeAndPrecision(string symbol)
        {
            return _lotSizeCache.TryGetValue(symbol, out var info) ? info : (0, 0, 0);
        }

        private decimal CalculateQuantity(decimal price, string symbol, decimal initialMargin)
        {
            decimal marginPerTrade = initialMargin;
            decimal quantity = (marginPerTrade * _leverage) / price;

            // Get the lot size and price precision for the symbol
            var (lotSize, pricePrecision, tickSize) = GetLotSizeAndPrecision(symbol);
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

        private async Task PlaceOrderAsync(string symbol, decimal price, bool isLong, string signal, long timestampEntry, decimal? takeProfit = null, decimal? trailingActivationPercent = null, decimal? trailingCallbackPercent = null)
        {
            lock (_activeTrades) // Lock to ensure thread safety
            {
                if ((isLong && _tradeDirection == SelectedTradeDirection.OnlyShorts) ||
                    (!isLong && _tradeDirection == SelectedTradeDirection.OnlyLongs))
                {
                    return; // Skipping trade due to trade direction preference
                }

                if (_activeTrades.Count >= 25)
                {
                    return; // Max active trades limit reached
                }

                if (_activeTrades.Values.Any(t => t.Symbol == symbol && t.IsInTrade))
                {
                    return; // Trade for symbol already active
                }
            }

            decimal takeProfitPrice;
            decimal stopLossPrice;
            decimal riskDistance;

            if (takeProfit.HasValue)
            {
                takeProfitPrice = takeProfit.Value;
                riskDistance = takeProfitPrice - price;
                stopLossPrice = isLong ? price - (riskDistance / 1m) : price + (riskDistance / 1m);
            }
            else
            {
                var (tpPercent, slPercent) = await CalculateATRBasedTPandSL(symbol);
                if (tpPercent == -1 || slPercent == -1) return;

                // Calculate the take profit and stop loss prices based on trade direction
                if (isLong)
                {
                    takeProfitPrice = price * (1 + (tpPercent / 100));
                    stopLossPrice = price * (1 - (slPercent / 100));
                }
                else // for short positions
                {
                    takeProfitPrice = price * (1 - (tpPercent / 100));
                    stopLossPrice = price * (1 + (slPercent / 100));
                }

                // Calculate risk distance
                riskDistance = isLong ? takeProfitPrice - price : price - takeProfitPrice;

                // Adjust the trailing activation to be 1/3 of the entry-to-TP distance
                // For longs, it's a percentage above the entry price
                // For shorts, it's a percentage below the entry price
                // Calculate trailing activation based on entry-to-TP distance

                // Set Trailing Activation
                trailingActivationPercent = isLong 
                                            ? (riskDistance / 2) / price * 100  // Above entry for longs
                                            : (riskDistance / 2) / price * 100; // Below entry for shorts

                // Set Callback Rate
                trailingCallbackPercent = isLong 
                                        ? (riskDistance) / price * 100  // Closer for longs to prevent early exit
                                        : (riskDistance) / price * 100; // Further away for shorts to avoid triggering too soon
            }

            decimal quantity = CalculateQuantity(price, symbol, _marginPerTrade);
            var trade = new Trade(_nextTradeId++, symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage, signal, _interval, timestampEntry);

            if (_operationMode == OperationMode.LiveRealTrading)
            {            
                await PlaceRealOrdersAsync(trade, trailingActivationPercent, trailingCallbackPercent); // Pass trailing params
                Console.WriteLine($"Trade {trade.Symbol} - TP: {takeProfitPrice}, SL: {stopLossPrice}, Quantity: {quantity}, Trailing Activation % {trailingActivationPercent}, Callback rate %: {trailingCallbackPercent}");
            }
            else
            {
                trade.IsInTrade = true;
                lock (_activeTrades)
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
                    if(currentValue <= 0 || initialValue <= 0 )
                        break;

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

                    Console.WriteLine($"-----------Trade for {trade.Symbol} closed.");
                    Console.WriteLine($"-----------Realized Return for {trade.Symbol}: {trade.Profit:P2}");
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

        private async Task PlaceRealOrdersAsync(Trade trade, decimal? trailingActivationPercent, decimal? trailingCallbackPercent)
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");            

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
                throw new Exception("API Key or Secret Key is missing");

            if (trade != null && !await IsSymbolInOpenPositionAsync(trade.Symbol))
            {
                await SetLeverageAsync(trade.Symbol, trade.Leverage, "ISOLATED");

                var orderType = trade.IsLong ? "BUY" : "SELL";
                decimal price = trade.EntryPrice;
                decimal quantity = CalculateQuantity(price, trade.Symbol, trade.InitialMargin);
                int lotSizePrecision = GetLotSizePrecision(trade.Symbol);
                string formattedQuantity = quantity.ToString("F" + lotSizePrecision, CultureInfo.InvariantCulture);

                long serverTime = await GetServerTimeAsync();

                // Step 1: Place the market order
                var orderRequest = new RestRequest("/fapi/v1/order", Method.Post);
                orderRequest.AddParameter("symbol", trade.Symbol);
                orderRequest.AddParameter("side", orderType);
                orderRequest.AddParameter("type", "MARKET");
                orderRequest.AddParameter("quantity", formattedQuantity);
                orderRequest.AddParameter("timestamp", serverTime);

                var queryString = orderRequest.Parameters
                    .Where(p => p.Type == ParameterType.GetOrPost)
                    .Select(p => $"{p.Name}={p.Value}")
                    .Aggregate((current, next) => $"{current}&{next}");

                var signature = GenerateSignature(queryString);
                orderRequest.AddParameter("signature", signature);
                orderRequest.AddHeader("X-MBX-APIKEY", apiKey);

                var response = await _client.ExecuteAsync(orderRequest);

                if (response.IsSuccessful)
                {
                    var orderData = JsonConvert.DeserializeObject<OrderResponse>(response.Content);
                    var orderId = orderData.orderId;
                    var qtyInTrade = orderData.executedQty;
                    Console.WriteLine($"{orderType} Order placed for {trade.Symbol} at {trade.EntryPrice}. Quantity: {qtyInTrade}, Order ID: {orderId}");

                    trade.IsInTrade = true;                    
                    
                    // Regenerate timestamp for the next request
                    serverTime = await GetServerTimeAsync(5005);

                    var pricePrecision = _lotSizeCache[trade.Symbol].pricePrecision;
                    string stopLossPriceString = trade.StopLossPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);
                    
                    // **Fetch the tick size for the symbol** to ensure the price aligns with tick size.
                    decimal tickSize = GetTickSize(trade.Symbol);
                    
                    // **Adjust the take profit price to align with the tick size.**
                    decimal roundedTakeProfitPrice = Math.Floor(trade.TakeProfitPrice / tickSize) * tickSize;
                    string takeProfitPriceString = roundedTakeProfitPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);

                    // Step 2: Place Stop Loss as a separate STOP_MARKET order
                    var stopLossRequest = new RestRequest("/fapi/v1/order", Method.Post);
                    stopLossRequest.AddParameter("symbol", trade.Symbol);
                    stopLossRequest.AddParameter("side", trade.IsLong ? "SELL" : "BUY"); // Opposite direction for SL
                    stopLossRequest.AddParameter("type", "STOP_MARKET");
                    stopLossRequest.AddParameter("stopPrice", stopLossPriceString);
                    stopLossRequest.AddParameter("closePosition", "true"); // Close the entire position
                    stopLossRequest.AddParameter("timestamp", serverTime);

                    var stopLossQueryString = stopLossRequest.Parameters
                        .Where(p2 => p2.Type == ParameterType.GetOrPost)
                        .Select(p2 => $"{p2.Name}={p2.Value}")
                        .Aggregate((current, next) => $"{current}&{next}");

                    var stopLossSignature = GenerateSignature(stopLossQueryString);
                    stopLossRequest.AddParameter("signature", stopLossSignature);
                    stopLossRequest.AddHeader("X-MBX-APIKEY", apiKey);

                    var stopLossResponse = await _client.ExecuteAsync(stopLossRequest);

                    if (stopLossResponse.IsSuccessful)
                    {
                        Console.WriteLine($"Stop Loss set for {trade.Symbol} at {trade.StopLossPrice}");

                        orderData = JsonConvert.DeserializeObject<OrderResponse>(stopLossResponse.Content);
                        orderId = orderData.orderId;
                    }
                    else
                    {
                        Console.WriteLine($"Failed to place Stop Loss for {trade.Symbol}. Error: {stopLossResponse.ErrorMessage}");
                        Console.WriteLine($"Response Status Code: {stopLossResponse.StatusCode}");
                        Console.WriteLine($"Response Content: {stopLossResponse.Content}");
                    }

                    // Regenerate timestamp for the next request
                    serverTime = await GetServerTimeAsync(5005);

                    // Step 3: Place Take Profit as a LIMIT order
                    pricePrecision = _lotSizeCache[trade.Symbol].pricePrecision;
                    tickSize = GetTickSize(trade.Symbol);
                    roundedTakeProfitPrice = Math.Floor(trade.TakeProfitPrice / tickSize) * tickSize;
                    takeProfitPriceString = roundedTakeProfitPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);

                    var takeProfitRequest = new RestRequest("/fapi/v1/order", Method.Post);
                    takeProfitRequest.AddParameter("symbol", trade.Symbol);
                    takeProfitRequest.AddParameter("side", trade.IsLong ? "SELL" : "BUY");
                    takeProfitRequest.AddParameter("type", "LIMIT");
                    takeProfitRequest.AddParameter("price", takeProfitPriceString);
                    takeProfitRequest.AddParameter("quantity", formattedQuantity);
                    takeProfitRequest.AddParameter("timeInForce", "GTC");
                    takeProfitRequest.AddParameter("timestamp", serverTime);

                    var takeProfitQueryString = takeProfitRequest.Parameters
                        .Where(p3 => p3.Type == ParameterType.GetOrPost)
                        .Select(p3 => $"{p3.Name}={p3.Value}")
                        .Aggregate((current, next) => $"{current}&{next}");

                    var takeProfitSignature = GenerateSignature(takeProfitQueryString);
                    takeProfitRequest.AddParameter("signature", takeProfitSignature);
                    takeProfitRequest.AddHeader("X-MBX-APIKEY", apiKey);

                    var takeProfitResponse = await _client.ExecuteAsync(takeProfitRequest);

                    if (takeProfitResponse.IsSuccessful)
                    {
                        Console.WriteLine($"Take Profit set for {trade.Symbol} at {roundedTakeProfitPrice}");
                        Console.WriteLine($"Trying to put a trailing SL {trade.Symbol} triggered at {trailingActivationPercent} ");
                        //await PlaceTrailingStopLossAsync(trade, trailingActivationPercent, trailingCallbackPercent, formattedQuantity, apiKey);
                    }
                    else
                    {
                        Console.WriteLine($"Failed to place Take Profit for {trade.Symbol}. Error: {takeProfitResponse.ErrorMessage}");
                        Console.WriteLine($"Response Content: {takeProfitResponse.Content}");
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to place {orderType} Order for {trade.Symbol}. Error: {response.ErrorMessage}");
                    Console.WriteLine($"Response Status Code: {response.StatusCode}");
                    Console.WriteLine($"Response Content: {response.Content}");
                }
            }
        } 

        private async Task PlaceTrailingStopLossAsync(Trade trade, decimal? trailingActivationPercent, decimal? trailingCallbackPercent, string formattedQuantity, string apiKey)
        {
            long serverTime = await GetServerTimeAsync();
            
            // Check if _lotSizeCache has the necessary price precision
            if (!_lotSizeCache.TryGetValue(trade.Symbol, out var symbolInfo))
            {
                throw new Exception($"No precision data found for {trade.Symbol}");
            }

            int pricePrecision = symbolInfo.pricePrecision;

            // Set up the trailing stop loss parameters
            var trailingStopLossRequest = new RestRequest("/fapi/v1/order", Method.Post);
            trailingStopLossRequest.AddParameter("symbol", trade.Symbol);
            trailingStopLossRequest.AddParameter("side", trade.IsLong ? "SELL" : "BUY");
            trailingStopLossRequest.AddParameter("type", "TRAILING_STOP_MARKET");            
            trailingStopLossRequest.AddParameter("quantity", formattedQuantity);

            decimal activationPrice = trade.EntryPrice * (1 + (trailingActivationPercent.Value / 100)); // Use .Value
            string activationPriceString = activationPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);
            string callbackRateString = trailingCallbackPercent.Value.ToString("F2", CultureInfo.InvariantCulture); // Use .Value
            
            trailingStopLossRequest.AddParameter("activationPrice", activationPriceString);
            trailingStopLossRequest.AddParameter("callbackRate", callbackRateString);
            //trailingStopLossRequest.AddParameter("closePosition", "TRUE");
            trailingStopLossRequest.AddParameter("timestamp", serverTime);

            // Generate signature and send request...
            var stopLossQueryString = trailingStopLossRequest.Parameters
                .Where(p => p.Type == ParameterType.GetOrPost)
                .Select(p => $"{p.Name}={p.Value}")
                .Aggregate((current, next) => $"{current}&{next}");

            var stopLossSignature = GenerateSignature(stopLossQueryString);
            trailingStopLossRequest.AddParameter("signature", stopLossSignature);
            trailingStopLossRequest.AddHeader("X-MBX-APIKEY", apiKey);

            var stopLossResponse = await _client.ExecuteAsync(trailingStopLossRequest);

            if (stopLossResponse.IsSuccessful)
            {
                Console.WriteLine($"Trailing SL activates for {trade.Symbol} at {activationPriceString}");                 
            }
            else
            {
                Console.WriteLine($"Failed to place trail SL for {trade.Symbol}. Error: {stopLossResponse.ErrorMessage}");
                Console.WriteLine($"Response Content: {stopLossResponse.Content}");
            }
        }

        private async Task<bool> IsSymbolInOpenPositionAsync(string symbol)
        {
            // CleanupResidualOrders(symbol);

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
        

        // Helper method to get the tick size for a symbol from the exchange info
        private decimal GetTickSize(string symbol)
        {
            if (_lotSizeCache.ContainsKey(symbol))
            {
                return _lotSizeCache[symbol].tickSize; // Assuming tickSize is stored in _lotSizeCache
            }
            throw new Exception($"Tick size not found for symbol {symbol}");
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
            //Console.WriteLine("Setting margin type:");
           //Console.WriteLine($"Symbol: {symbol}, Margin Type: {marginType}, Timestamp: {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

            // Execute the request and log the response
            var marginTypeResponse = await client.ExecuteAsync(marginTypeRequest);
            //Console.WriteLine("Response Content: " + marginTypeResponse.Content);

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

        public async Task<long> GetServerTimeAsync(int delayMilliseconds = 100)
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