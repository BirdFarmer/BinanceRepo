using Serilog;
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using BinanceLive.Tools;
using Microsoft.Extensions.Logging;
using SQLitePCL;

namespace BinanceTestnet.Trading
{
    public interface IExchangeInfoProvider
    {
        Task<ExchangeInfo> GetExchangeInfoAsync();
    }

    public class OrderManager
    {
        public enum ExitMode
        {
            TakeProfit,
            TrailingStop,
            PnLPct
        }

        // Exit mode runtime config (default: TakeProfit)
        private ExitMode _exitMode = ExitMode.TakeProfit;
        // Exit PnL percent (unlevered) - session/runtime only
        private decimal? _exitPnLPct = null;
        // Fields grouped by functionality
        private Wallet _wallet;
        private readonly DatabaseManager _databaseManager;
        private readonly RestClient _client;
        //private readonly ExcelWriter _excelWriter;
        private readonly TradeLogger _tradeLogger;
        private readonly SelectedTradeDirection _tradeDirection;
        private readonly SelectedTradingStrategy _tradingStrategy;
        
        // Trading parameters
        private decimal _leverage;
        private decimal _takeProfit;
        private decimal _stopLoss;
        private decimal? _tpIteration;
        private string _interval;
        private OperationMode _operationMode;
        
        // Trade statistics
        private readonly ConcurrentDictionary<int, Trade> _activeTrades = new ConcurrentDictionary<int, Trade>();
        //private Dictionary<string, (decimal lotSize, int pricePrecision, decimal tickSize)> _lotSizeCache = new Dictionary<string, (decimal, int, decimal)>();
        private readonly ConcurrentDictionary<string, (decimal lotSize, int pricePrecision, decimal tickSize)> _lotSizeCache;

        private int _nextTradeId = 1;
        private decimal noOfTrades = 0;
        private decimal profitOfClosed = 0;
        private decimal longs = 0;
        private decimal shorts = 0;
        private decimal _marginPerTrade = 0;
        public DatabaseManager DatabaseManager => _databaseManager;
        private readonly string _sessionId;
        private readonly IExchangeInfoProvider _exchangeInfo;
        private readonly ILogger<OrderManager> _logger;

        private readonly Action<string, bool, string, decimal, DateTime> _onTradeEntered;
        
    // Trailing configuration
    // When enabled, TP is replaced by trailing. Activation value is treated as an ATR multiplier and
    // converted per-symbol to a percent using ATR/Price at order time.
    private bool _replaceTakeProfitWithTrailing = false;
    private decimal? _trailingActivationPercentOverride = null; // semantics: ATR multiplier
    private decimal? _trailingCallbackPercentOverride = null;
    

        public OrderManager(Wallet wallet, decimal leverage, OperationMode operationMode,
                        string interval, decimal takeProfit, decimal stopLoss,
                        SelectedTradeDirection tradeDirection, SelectedTradingStrategy tradingStrategy,
                        RestClient client, decimal tpIteration, decimal margin, string databasePath, 
                        string sessionId, IExchangeInfoProvider exchangeInfoProvider, ILogger<OrderManager> logger,
                        Action<string, bool, string, decimal, DateTime> onTradeEntered = null)
        {
            _wallet = wallet;
            _databaseManager = new DatabaseManager(databasePath); // Create a new instance here
            _leverage = leverage;

            // Add this line
            _tradeLogger = new TradeLogger(databasePath);

            _operationMode = operationMode;
            _interval = interval;
            _takeProfit = takeProfit;
            _stopLoss = stopLoss;
            _tpIteration = tpIteration;
            _tradeDirection = tradeDirection;
            _tradingStrategy = tradingStrategy;
            _client = client;
            _marginPerTrade = margin;
            //_lotSizeCache = new Dictionary<string, (decimal, int, decimal)>();        
            _lotSizeCache = new ConcurrentDictionary<string, (decimal, int, decimal)>();

            _logger = logger;     
            _onTradeEntered = onTradeEntered;
            _sessionId = sessionId; // Store the SessionId
            DatabaseManager.InitializeDatabase();
            
            _exchangeInfo = exchangeInfoProvider;
            _ = InitializeLotSizes(); // Start async initialization (fire-and-forget)

            // Remove this line
            // _excelWriter.Initialize(fileName);

            //InitializeLotSizes().Wait(); // Awaiting the task for initialization
        }

        public void UpdateTrailingConfig(bool replaceTpWithTrailing, decimal? activationPercent = null, decimal? callbackPercent = null)
        {
            _replaceTakeProfitWithTrailing = replaceTpWithTrailing;
            _trailingActivationPercentOverride = activationPercent; // store ATR multiplier
            _trailingCallbackPercentOverride = callbackPercent;
        }

        // Update exit mode and optional PnL percent (runtime only)
        public void UpdateExitMode(ExitMode mode, decimal? exitPnLPct = null)
        {
            _exitMode = mode;
            _exitPnLPct = exitPnLPct;
        }

        public async Task InitializeLotSizes()
        {
            const int maxRetries = 3;
            int attempt = 0;
            bool success = false;

            while (attempt < maxRetries && !success)
            {
                attempt++;
                _logger.LogInformation($"Initializing lot sizes (Attempt {attempt}/{maxRetries})");

                try
                {
                    // 1. Fetch exchange info with timeout protection
                    var exchangeInfo = await _exchangeInfo.GetExchangeInfoAsync();
                    
                    if (exchangeInfo?.Symbols == null)
                    {
                        _logger.LogWarning("Received empty exchange info");
                        continue;
                    }

                    // 2. Clear existing cache
                    _lotSizeCache.Clear(); 

                    // 3. Process all symbols
                    int processedCount = 0;
                    foreach (var symbolInfo in exchangeInfo.Symbols)
                    {
                        try
                        {
                            var lotSizeFilter = symbolInfo.Filters?.FirstOrDefault(f => f.FilterType == "LOT_SIZE");
                            var priceFilter = symbolInfo.Filters?.FirstOrDefault(f => f.FilterType == "PRICE_FILTER");

                            if (lotSizeFilter != null && priceFilter != null)
                            {
                                _lotSizeCache[symbolInfo.Symbol] = (
                                    StepSize: decimal.Parse(lotSizeFilter.StepSize, CultureInfo.InvariantCulture),
                                    PricePrecision: symbolInfo.PricePrecision,
                                    TickSize: decimal.Parse(priceFilter.TickSize, CultureInfo.InvariantCulture)
                                );
                                processedCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, $"Failed to process symbol {symbolInfo.Symbol}");
                        }
                    }

                    _logger.LogInformation($"Successfully cached lot sizes for {processedCount} symbols");
                    success = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Attempt {attempt} failed");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000 * attempt); // Exponential backoff
                    }
                }
            }

            if (!success)
            {
                _logger.LogCritical("Failed to initialize lot sizes after maximum retries");
                throw new Exception("Lot size initialization failed");
            }
        }

        public decimal GetTakeProfit()
        {
            return _takeProfit;
        }

        public string GetInterval()
        {
            return _interval;
        }

        public decimal GetMarginPerTrade()
        {
            return _marginPerTrade;
        }


        public decimal GetStopLoss()
        {
            return _stopLoss;
        }

        public decimal GetLeverage()
        {
            return _leverage;
        }   

        public OperationMode GetOperationMode()
        {
            return _operationMode;
        }

        public SelectedTradingStrategy GetStrategy()
        {
            return _tradingStrategy;
        }   

        public (decimal lotSize, int pricePrecision, decimal tickSize) GetLotSizeAndPrecision(string symbol)
        {
            return _lotSizeCache.TryGetValue(symbol, out var info) ? info : (0, 0, 0);
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

        public async Task PlaceLongOrderAsync(string symbol, decimal price, string signal, long timestamp, decimal? takeProfit = null, decimal? explicitStopLoss = null)
        {
            await PlaceOrderAsync(symbol, price, true, signal, timestamp, takeProfit, explicitStopLoss);

        }

        public async Task PlaceShortOrderAsync(string symbol, decimal price, string signal, long timestamp, decimal? takeProfit = null, decimal? explicitStopLoss = null)
        {
            await PlaceOrderAsync(symbol, price, false, signal, timestamp, takeProfit, explicitStopLoss);
        }

    private async Task PlaceOrderAsync(string symbol, decimal price, bool isLong, string signal, long timestampEntry, 
                    decimal? takeProfit = null, decimal? explicitStopLoss = null, decimal? trailingActivationPercent = null, 
                    decimal? trailingCallbackPercent = null)
        {
            lock (_activeTrades)
            {
                if ((isLong && _tradeDirection == SelectedTradeDirection.OnlyShorts) ||
                    (!isLong && _tradeDirection == SelectedTradeDirection.OnlyLongs))
                {
                    return; // Skipping trade due to trade direction preference
                }

                if (_activeTrades.Count >= 8)
                {
                    
                    _logger.LogInformation($"Skip trade, maximum active trades reached: {_activeTrades.Count}");
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

            // Track whether caller provided an explicit stop loss (e.g., London POC)
            bool explicitSLProvided = explicitStopLoss.HasValue;

            // If an explicit stop loss is provided (e.g., POC), use it and derive TP to satisfy 2:1 RR unless a TP is explicitly provided.
            if (explicitStopLoss.HasValue)
            {
                stopLossPrice = explicitStopLoss.Value;
                if (isLong)
                {
                    riskDistance = price - stopLossPrice;
                    if (takeProfit.HasValue)
                    {
                        takeProfitPrice = takeProfit.Value;
                    }
                    else
                    {
                        takeProfitPrice = price + 2m * riskDistance; // 2:1 RR
                    }
                }
                else
                {
                    riskDistance = stopLossPrice - price;
                    if (takeProfit.HasValue)
                    {
                        takeProfitPrice = takeProfit.Value;
                    }
                    else
                    {
                        takeProfitPrice = price - 2m * riskDistance; // 2:1 RR for shorts
                    }
                }
            }
            else if (takeProfit.HasValue)
            {
                takeProfitPrice = takeProfit.Value;
                riskDistance = takeProfitPrice - price;
                stopLossPrice = isLong ? price - (riskDistance / 1m) : price + (riskDistance / 1m);
            }
            else
            {
                // If Exit Mode is PnLPct, compute TP from unlevered PnL percent and keep SL from ATR-based logic
                if (_exitMode == ExitMode.PnLPct && _exitPnLPct.HasValue && _exitPnLPct.Value > 0)
                {
                    var exitPct = _exitPnLPct.Value;
                    var rrDivider = _stopLoss > 0 ? _stopLoss : 2.0m;
                    var slPercent = exitPct / rrDivider; // e.g. TP 5% and RR 2 => SL 2.5%

                    if (isLong)
                    {
                        takeProfitPrice = price * (1 + (exitPct / 100m));
                        stopLossPrice = price * (1 - (slPercent / 100m));
                    }
                    else
                    {
                        takeProfitPrice = price * (1 - (exitPct / 100m));
                        stopLossPrice = price * (1 + (slPercent / 100m));
                    }

                    riskDistance = isLong ? takeProfitPrice - price : price - takeProfitPrice;
                    trailingActivationPercent = (riskDistance / 2) / price * 100;
                    trailingCallbackPercent = (riskDistance) / price * 100;
                }
                else
                {
                    var (tpPercent, slPercent) = await CalculateATRBasedTPandSL(symbol);
                    if (tpPercent <= 0 || slPercent <= 0 || double.IsNaN((double)tpPercent) || 
                        double.IsNaN((double)slPercent) || double.IsInfinity((double)tpPercent) || 
                        double.IsInfinity((double)slPercent))
                    {
                        return; // Skip the trade if values are invalid
                    }

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

                    riskDistance = isLong ? takeProfitPrice - price : price - takeProfitPrice;
                    trailingActivationPercent = (riskDistance / 2) / price * 100;
                    trailingCallbackPercent = (riskDistance) / price * 100;
                }
            }

            // If trailing is replacing TP, derive activation percent using ATR/Price and SL using RR divider
            if (_replaceTakeProfitWithTrailing && !explicitSLProvided)
            {
                // Compute activation percent from ATR/Price if possible
                decimal? derivedActivationPct = null;
                decimal? atrToPriceLogged = null;
                decimal atrMultLogged = 0m;
                try
                {
                    var atrToPrice = await GetAtrToPriceAsync(symbol);
                    if (atrToPrice.HasValue && atrToPrice.Value > 0)
                    {
                        var atrMult = Math.Abs(_trailingActivationPercentOverride ?? 1.0m);
                        atrToPriceLogged = atrToPrice.Value;
                        atrMultLogged = atrMult;
                        derivedActivationPct = Math.Max(0.05m, atrToPrice.Value * atrMult * 100m); // clamp to min 0.05%
                    }
                }
                catch { /* fallback below */ }

                var activationPct = Math.Abs(derivedActivationPct ?? trailingActivationPercent ?? 1.0m);
                trailingActivationPercent = activationPct; // propagate for live order placement
                var rrDivider = _stopLoss > 0 ? _stopLoss : 2.0m; // interpret _stopLoss as Risk-Reward divider in trailing mode
                var slDistance = (activationPct / 100m) * price / rrDivider;
                stopLossPrice = isLong ? price - slDistance : price + slDistance;

                // DEBUG: Print trailing math details
                try
                {
                    if (atrToPriceLogged.HasValue)
                    {
                        var atrAbs = atrToPriceLogged.Value * price; // ATR in price units
                        var dAbs = atrAbs * (atrMultLogged == 0m ? 1m : atrMultLogged); // absolute activation distance
                        var actPrice = isLong ? price + dAbs : price - dAbs; // activation trigger price
                        _logger.LogInformation(
                            "[TrailingMath] {Symbol} {Side} entry={Entry:F6} atr%={AtrPct:F4}% atrAbs={AtrAbs:F6} atrMult={AtrMult:F2} derivedAct%={ActPct:F4}% dAbs={DAbs:F6} actPrice={ActPrice:F6} rr={RR:F2} slDist={SLDist:F6} stopLoss={SL:F6}",
                            symbol,
                            isLong ? "LONG" : "SHORT",
                            price,
                            atrToPriceLogged.Value * 100m,
                            atrAbs,
                            atrMultLogged,
                            activationPct,
                            dAbs,
                            actPrice,
                            rrDivider,
                            slDistance,
                            stopLossPrice
                        );
                    }
                    else
                    {
                        var dAbs = (activationPct / 100m) * price; // absolute activation distance (no ATR path)
                        var actPrice = isLong ? price + dAbs : price - dAbs; // activation trigger price
                        _logger.LogInformation(
                            "[TrailingMath] {Symbol} {Side} entry={Entry:F6} (no ATR) usedAct%={ActPct:F4}% dAbs={DAbs:F6} actPrice={ActPrice:F6} rr={RR:F2} slDist={SLDist:F6} stopLoss={SL:F6}",
                            symbol,
                            isLong ? "LONG" : "SHORT",
                            price,
                            activationPct,
                            dAbs,
                            actPrice,
                            rrDivider,
                            slDistance,
                            stopLossPrice
                        );
                    }
                }
                catch { }
            }

            decimal quantity = CalculateQuantity(price, symbol, _marginPerTrade);
            
            // Calculate liquidation price and maintenance margin rate
            decimal maintenanceMarginRate = 0.004m; // 0.4% - typical for Binance
            decimal liquidationPrice;
            if (isLong)
            {
                liquidationPrice = price * (1 - (1 / _leverage) + maintenanceMarginRate);
            }
            else
            {
                liquidationPrice = price * (1 + (1 / _leverage) - maintenanceMarginRate);
            }
            
            // Adjust stop loss if it would cause liquidation
            decimal buffer = price * 0.001m; // 0.1% buffer
            if (isLong)
            {
                if (stopLossPrice <= liquidationPrice)
                {
                    decimal newStopLoss = liquidationPrice + buffer;
                    //_logger.LogInformation($"Adjusted SL for {symbol} long from {stopLossPrice} to {newStopLoss} to avoid liquidation");
                    stopLossPrice = newStopLoss;
                }
            }
            else
            {
                if (stopLossPrice >= liquidationPrice)
                {
                    decimal newStopLoss = liquidationPrice - buffer;
                    //_logger.LogInformation($"Adjusted SL for {symbol} short from {stopLossPrice} to {newStopLoss} to avoid liquidation");
                    stopLossPrice = newStopLoss;
                }
            }

            // Create a new Trade object with liquidation information
            var trade = new Trade(
                tradeId: 0, // Let SQLite generate the TradeId
                sessionId: _sessionId,
                symbol: symbol,
                entryPrice: price,
                takeProfitPrice: takeProfitPrice,
                stopLossPrice: stopLossPrice,
                quantity: quantity,
                isLong: isLong,
                leverage: _leverage,
                signal: signal,
                interval: _interval,
                timestamp: timestampEntry,
                takeProfitMultiplier: _tpIteration,
                marginPerTrade: _marginPerTrade,
                liquidationPrice: liquidationPrice,
                maintenanceMarginRate: maintenanceMarginRate
            );
            
            // Set trailing simulation parameters on the trade (used in paper/backtest; harmless in live)
            // Do not enable trailing simulation for trades where the strategy explicitly provided an SL.
            if (_replaceTakeProfitWithTrailing && !explicitSLProvided)
            {
                var activationPctLocal = Math.Abs(trailingActivationPercent ?? _trailingActivationPercentOverride ?? 1.0m);
                var callbackPctLocal = Math.Abs(_trailingCallbackPercentOverride ?? trailingCallbackPercent ?? 1.0m);
                // clamp callback to [0.1, 5.0]
                callbackPctLocal = Math.Min(5.0m, Math.Max(0.1m, callbackPctLocal));
                trade.TrailingEnabled = true;
                trade.TrailingActivationPercent = activationPctLocal;
                trade.TrailingCallbackPercent = callbackPctLocal;
                trade.TrailingActivated = false;
                trade.TrailingActivationPrice = null;
                trade.TrailingExtreme = null;
            }
        
            // NOTIFY VIA CALLBACK (pass individual values)
            // For paper/backtest, show immediately; for live, defer until after successful order
            if (_operationMode != OperationMode.LiveRealTrading)
            {
                DateTime entryTime;
                try
                {
                    entryTime = timestampEntry > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(timestampEntry).UtcDateTime
                        : DateTime.UtcNow;
                }
                catch
                {
                    entryTime = DateTime.UtcNow;
                }

                _onTradeEntered?.Invoke(symbol, isLong, signal, price, entryTime);
            }
        

            if (_operationMode == OperationMode.LiveRealTrading)
            {
                await PlaceRealOrdersAsync(trade, trailingActivationPercent, trailingCallbackPercent, explicitSLProvided);
            }
            else
            {
                trade.IsInTrade = true;
                lock (_activeTrades)
                {
                    if (_wallet.PlaceTrade(trade))
                    {
                        _logger?.LogDebug($"With signal: {signal}");
                        if (trade.IsLong) longs++;
                        else shorts++;
                        noOfTrades++;

                        // Log the trade to the database and retrieve the auto-generated TradeId
                        int tradeId = _tradeLogger.LogOpenTrade(trade, _sessionId);
                        if (tradeId != -1)
                        {
                            trade.TradeId = tradeId; // Update the Trade object with the new TradeId
                            _activeTrades[trade.TradeId] = trade; // Add the trade to active trades
                        }
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
            return VolatilityBasedTPandSL.CalculateTpAndSlBasedOnAtrMultiplier(symbol, symbolQuotes, _takeProfit, _stopLoss);
        }

        // Helper to compute ATR/Price ratio for a symbol on the current interval
        private async Task<decimal?> GetAtrToPriceAsync(string symbol)
        {
            try
            {
                var history = await DataFetchingUtility.FetchHistoricalData(_client, symbol, _interval);
                var quotes = history.Select(k => new Skender.Stock.Indicators.Quote
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).DateTime,
                    Open = k.Open,
                    High = k.High,
                    Low = k.Low,
                    Close = k.Close,
                    Volume = k.Volume
                }).ToList();

                const int AtrPeriod = 14;
                if (quotes.Count < AtrPeriod) return null;
                var atrSeries = quotes.GetAtr(AtrPeriod).Where(a => a.Atr.HasValue).Select(a => a.Atr!.Value).ToList();
                if (atrSeries.Count == 0) return null;
                decimal currentAtr = (decimal)atrSeries.Last();
                decimal currentPrice = quotes.Last().Close;
                if (currentPrice <= 0) return null;
                return currentAtr / currentPrice;
            }
            catch
            {
                return null;
            }
        }

        public List<Trade> GetActiveTrades()
        {
            lock (_activeTrades)
            {
                return _activeTrades.Values.Where(t => t.IsInTrade).ToList();
            }
        }

        public async Task CheckAndCloseTrades(Dictionary<string, decimal> currentPrices, long closeTime = 0)
        {
            foreach (var trade in _activeTrades.Values.ToList())
            {
                if (trade == null || currentPrices == null || !currentPrices.ContainsKey(trade.Symbol))
                {
                    continue;
                }

                var currentPrice = currentPrices[trade.Symbol];
                
                // Check if price has crossed SL or TP at any point during the candle
                bool shouldClose = false;
                decimal closingPrice = currentPrice;
                
                if (trade.IsLong)
                {
                    // For long positions: always honor SL
                    if (currentPrice <= trade.StopLoss)
                    {
                        shouldClose = true;
                        closingPrice = trade.StopLoss; // Use the exact SL price
                        _logger.LogInformation($"Closing {trade.Symbol} LONG at Stop Loss: {closingPrice}");
                    }
                    else if (trade.TrailingEnabled)
                    {
                        // Trailing simulation
                        var actPct = Math.Abs(trade.TrailingActivationPercent ?? 1.0m);
                        var cbPct = Math.Min(5.0m, Math.Max(0.1m, Math.Abs(trade.TrailingCallbackPercent ?? 1.0m)));
                        var activationPrice = trade.TrailingActivationPrice ?? (trade.EntryPrice * (1 + (actPct / 100m)));

                        if (!trade.TrailingActivated)
                        {
                            // Activate when price moves up to activation price
                            if (currentPrice >= activationPrice)
                            {
                                trade.TrailingActivated = true;
                                trade.TrailingActivationPrice = activationPrice;
                                trade.TrailingExtreme = currentPrice; // peak tracker
                                _logger.LogInformation($"{trade.Symbol} LONG trailing activated at {activationPrice:F6} (cb {cbPct:F1}%)");
                            }
                        }
                        else
                        {
                            // Update peak and check retrace
                            if (!trade.TrailingExtreme.HasValue || currentPrice > trade.TrailingExtreme.Value)
                            {
                                trade.TrailingExtreme = currentPrice;
                            }
                            var retraceTrigger = trade.TrailingExtreme.Value * (1 - (cbPct / 100m));
                            if (currentPrice <= retraceTrigger)
                            {
                                shouldClose = true;
                                closingPrice = currentPrice;
                                _logger.LogInformation($"Closing {trade.Symbol} LONG at trailing retrace: {closingPrice:F6} (peak {trade.TrailingExtreme.Value:F6}, cb {cbPct:F1}%)");
                            }
                        }
                    }
                    else if (currentPrice >= trade.TakeProfit)
                    {
                        // Standard TP when trailing not enabled
                        shouldClose = true;
                        closingPrice = trade.TakeProfit; // Use the exact TP price
                        _logger.LogInformation($"Closing {trade.Symbol} LONG at Take Profit: {closingPrice}");
                    }
                }
                else
                {
                    // For short positions: always honor SL
                    if (currentPrice >= trade.StopLoss)
                    {
                        shouldClose = true;
                        closingPrice = trade.StopLoss; // Use the exact SL price
                        _logger.LogInformation($"Closing {trade.Symbol} SHORT at Stop Loss: {closingPrice}");
                    }
                    else if (trade.TrailingEnabled)
                    {
                        // Trailing simulation
                        var actPct = Math.Abs(trade.TrailingActivationPercent ?? 1.0m);
                        var cbPct = Math.Min(5.0m, Math.Max(0.1m, Math.Abs(trade.TrailingCallbackPercent ?? 1.0m)));
                        var activationPrice = trade.TrailingActivationPrice ?? (trade.EntryPrice * (1 - (actPct / 100m)));

                        if (!trade.TrailingActivated)
                        {
                            // Activate when price moves down to activation price
                            if (currentPrice <= activationPrice)
                            {
                                trade.TrailingActivated = true;
                                trade.TrailingActivationPrice = activationPrice;
                                trade.TrailingExtreme = currentPrice; // trough tracker
                                _logger.LogInformation($"{trade.Symbol} SHORT trailing activated at {activationPrice:F6} (cb {cbPct:F1}%)");
                            }
                        }
                        else
                        {
                            // Update trough and check retrace
                            if (!trade.TrailingExtreme.HasValue || currentPrice < trade.TrailingExtreme.Value)
                            {
                                trade.TrailingExtreme = currentPrice;
                            }
                            var retraceTrigger = trade.TrailingExtreme.Value * (1 + (cbPct / 100m));
                            if (currentPrice >= retraceTrigger)
                            {
                                shouldClose = true;
                                closingPrice = currentPrice;
                                _logger.LogInformation($"Closing {trade.Symbol} SHORT at trailing retrace: {closingPrice:F6} (trough {trade.TrailingExtreme.Value:F6}, cb {cbPct:F1}%)");
                            }
                        }
                    }
                    else if (currentPrice <= trade.TakeProfit)
                    {
                        // Standard TP when trailing not enabled
                        shouldClose = true;
                        closingPrice = trade.TakeProfit; // Use the exact TP price
                        _logger.LogInformation($"Closing {trade.Symbol} SHORT at Take Profit: {closingPrice}");
                    }
                }

                if (!shouldClose)
                {
                    continue;
                }

                // Determine exit time
                DateTime exitTime;
                if (closeTime == 0)
                {
                    exitTime = DateTime.UtcNow;
                }
                else
                {
                    exitTime = DateTimeOffset.FromUnixTimeMilliseconds(closeTime).UtcDateTime;
                }

                // Close the trade
                trade.CloseTrade(closingPrice, exitTime);

                // Calculate P&L for the trade (this is just the price movement component)
                decimal priceMovementPnL;
                if (trade.IsLong)
                {
                    priceMovementPnL = (closingPrice - trade.EntryPrice) * trade.Quantity;
                }
                else
                {
                    priceMovementPnL = (trade.EntryPrice - closingPrice) * trade.Quantity;
                }

                // Total funds change = initial margin returned + P&L
                decimal totalFundsChange = trade.InitialMargin + priceMovementPnL;

                // Update wallet and tracking
                _wallet.AddFunds(totalFundsChange);
                profitOfClosed += priceMovementPnL; // Track just the P&L component for statistics

                // Remove from active trades
                _activeTrades.TryRemove(trade.TradeId, out _);

                // Log to database
                _tradeLogger.LogCloseTrade(trade, _sessionId);

                await Task.CompletedTask;
            }
        }

        private bool ShouldCloseTrade(Trade trade, decimal currentPrice)
        {
            return trade.IsLong
                ? (currentPrice >= trade.TakeProfit || currentPrice <= trade.StopLoss)
                : (currentPrice <= trade.TakeProfit || currentPrice >= trade.StopLoss);
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
                    string entryTime = TimeTools.FormatTimestamp(trade.EntryTime); // Use the helper function
                    if (trade.TrailingEnabled)
                    {
                        Console.WriteLine($"{trade.Symbol}: {direction}, Entry Price: {trade.EntryPrice:F5}, @ {entryTime}, Trailing: Act={trade.TrailingActivationPercent:F1}% Cb={trade.TrailingCallbackPercent:F1}%, Stop Loss: {trade.StopLoss:F5}, Leverage: {trade.Leverage:F1}, Current Price: {currentPrices[trade.Symbol]:F5}, Realized Return: {realizedReturn:F2}%");
                        logOutput += $"{trade.Symbol}: Direction: {direction}, Entry Price: {trade.EntryPrice:F5}, Current Price: {currentPrices[trade.Symbol]:F5}, Trailing: Act={trade.TrailingActivationPercent:F1}% Cb={trade.TrailingCallbackPercent:F1}%, Stop Loss: {trade.StopLoss:F5}, Leverage: {trade.Leverage:F1}, Realized Return: {realizedReturn:F2}%\n";
                    }
                    else
                    {
                        Console.WriteLine($"{trade.Symbol}: {direction}, Entry Price: {trade.EntryPrice:F5}, @ {entryTime}, Take Profit: {trade.TakeProfit:F5}, Stop Loss: {trade.StopLoss:F5}, Leverage: {trade.Leverage:F1}, Current Price: {currentPrices[trade.Symbol]:F5}, Realized Return: {realizedReturn:F2}%");
                        logOutput += $"{trade.Symbol}: Direction: {direction}, Entry Price: {trade.EntryPrice:F5}, Current Price: {currentPrices[trade.Symbol]:F5}, Take Profit: {trade.TakeProfit:F5}, Stop Loss: {trade.StopLoss:F5}, Leverage: {trade.Leverage:F1}, Realized Return: {realizedReturn:F2}%\n";
                    }
                }
            }

            Log.Debug(logOutput);
        }

        public void PrintTradeSummary()
        {
            Log.Debug($"No. of Trades: {noOfTrades}");
            Log.Debug($"Longs: {longs}");
            Log.Debug($"Shorts: {shorts}");
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

        public void CloseAllActiveTrades(Dictionary<string, decimal> currentPrices, long closeTime)
        {
            // Create a copy of _activeTrades for iteration
            var tradesToClose = _activeTrades.Values.ToList();

            foreach (var trade in tradesToClose)
            {
                if (!trade.IsClosed)
                {
                    // Determine the exit time
                    DateTime exitTime;
                    if (closeTime == 0)
                    {
                        // Live trading: Use current UTC time
                        exitTime = DateTime.UtcNow;
                    }
                    else
                    {
                        // Determine if closeTime is in Ticks or Unix timestamp
                        if (closeTime > 253402300799999) // If closeTime is in Ticks
                        {
                            // Convert Ticks to DateTime
                            exitTime = new DateTime(closeTime, DateTimeKind.Utc);
                        }
                        else // If closeTime is a Unix timestamp
                        {
                            // Convert Unix timestamp to DateTime
                            exitTime = DateTimeOffset.FromUnixTimeMilliseconds(closeTime).UtcDateTime;
                        }
                    }

                    // Get the closePrice for this trade's symbol
                    if (currentPrices.TryGetValue(trade.Symbol, out decimal closePrice))
                    {
                        trade.CloseTrade(closePrice, exitTime);

                        var profit = trade.IsLong
                            ? (trade.Quantity * (closePrice - trade.EntryPrice)) + trade.InitialMargin
                            : (trade.Quantity * (trade.EntryPrice - closePrice)) + trade.InitialMargin;

                        Console.WriteLine($"-----------Trade for {trade.Symbol} closed.");
                        Console.WriteLine($"-----------Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                        _wallet.AddFunds(profit);
                        profitOfClosed += profit;

                        // Remove the trade from _activeTrades after closing it
                        _activeTrades.TryRemove(trade.TradeId, out _);

                        // Log the closed trade to the database
                        _tradeLogger.LogCloseTrade(trade, _sessionId);
                    }
                    else
                    {
                        Console.WriteLine($"Error: No price found for symbol {trade.Symbol}.");
                    }
                }
            }
        }      

        public void CloseAllActiveTradesForBacktest(decimal closePrice, long closeTime)
        {
            // Create a copy of _activeTrades for iteration
            var tradesToClose = _activeTrades.Values.ToList();

            foreach (var trade in tradesToClose)
            {
                if (!trade.IsClosed)
                {
                    // Determine the exit time
                    DateTime exitTime;
                    if (closeTime == 0)
                    {
                        // Live trading: Use current UTC time
                        exitTime = DateTime.UtcNow;
                    }
                    else
                    {
                        // Determine if closeTime is in Ticks or Unix timestamp
                        if (closeTime > 253402300799999) // If closeTime is in Ticks
                        {
                            // Convert Ticks to DateTime
                            exitTime = new DateTime(closeTime, DateTimeKind.Utc);
                        }
                        else // If closeTime is a Unix timestamp
                        {
                            // Convert Unix timestamp to DateTime
                            exitTime = DateTimeOffset.FromUnixTimeMilliseconds(closeTime).UtcDateTime;
                        }
                    }

                    // Use the closePrice passed to the method
                    decimal actualClosePrice = closePrice == -1 ? trade.EntryPrice : closePrice;

                    trade.CloseTrade(actualClosePrice, exitTime);

                    var profit = trade.IsLong
                        ? (trade.Quantity * (actualClosePrice - trade.EntryPrice)) + trade.InitialMargin
                        : (trade.Quantity * (trade.EntryPrice - actualClosePrice)) + trade.InitialMargin;

                    Console.WriteLine($"-----------Trade for {trade.Symbol} closed.");
                    Console.WriteLine($"-----------Realized Return for {trade.Symbol}: {trade.Profit:P2}");
                    _wallet.AddFunds(profit);
                    profitOfClosed += profit;

                    // Remove the trade from _activeTrades after closing it
                    _activeTrades.TryRemove(trade.TradeId, out _);

                    // Log the closed trade to the database
                    _tradeLogger.LogCloseTrade(trade, _sessionId);
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

        private async Task PlaceRealOrdersAsync(Trade trade, decimal? trailingActivationPercent, decimal? trailingCallbackPercent, bool explicitStopLossProvided)
        {
            string? apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY");
            string? secretKey = Environment.GetEnvironmentVariable("BINANCE_API_SECRET");            

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
                throw new Exception("API Key or Secret Key is missing");

            if (trade != null && !await IsSymbolInOpenPositionAsync(trade.Symbol))
            {
                await SetLeverageAsync(trade.Symbol, trade.Leverage, "ISOLATED");
                await DelayAsync(200); // Small delay after leverage update

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
                await DelayAsync(300); // Delay after market order

                if (response.IsSuccessful)
                {
                    // Deserialize the response to get order details
                    OrderResponse orderData = null;
                    try
                    {
                        orderData = JsonConvert.DeserializeObject<OrderResponse>(response.Content);
                    }
                    catch
                    {
                        // Swallow deserialization issues; we'll still proceed with our own price
                    }

                    var orderId = orderData?.orderId ?? 0;
                    var qtyInTrade = orderData?.executedQty ?? 0;
                    Console.WriteLine($"{orderType} Order placed for {trade.Symbol} at {trade.EntryPrice} with strategy {trade.Signal}. \nQuantity: {qtyInTrade}, Order ID: {orderId}");

                    trade.IsInTrade = true;                    
                    
                    // For live trading, add to Recent Trades after a successful order response
                    // Ignore any negative/failed paths entirely (no UI entry). Use our own entry price.
                    if (_operationMode == OperationMode.LiveRealTrading)
                    {
                        DateTime entryTime = trade.EntryTime.ToUniversalTime();
                        _onTradeEntered?.Invoke(trade.Symbol, trade.IsLong, trade.Signal, trade.EntryPrice, entryTime);
                    }
                    
                    
                    // Regenerate timestamp for the next request
                    serverTime = await GetServerTimeAsync(5005);
                    await DelayAsync(200); // Small delay after timestamp refresh

                    var pricePrecision = _lotSizeCache[trade.Symbol].pricePrecision;
                    string stopLossPriceString = trade.StopLoss.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);
                    
                    // Step 2: Place Stop Loss as a separate STOP_MARKET order
                    // Route conditional orders to the Algo Order API helper. This currently posts
                    // to `/fapi/v1/algoOrder`. The helper returns an AlgoOrderResult compatible
                    // with the old response handling so the rest of the logic remains unchanged.
                    var algoParams = new Dictionary<string, string>
                    {
                        { "symbol", trade.Symbol },
                        { "side", trade.IsLong ? "SELL" : "BUY" },
                        { "type", "STOP_MARKET" },
                        { "algoType", "CONDITIONAL" },
                        { "triggerPrice", stopLossPriceString },
                        { "closePosition", "true" }
                    };

                    var stopLossResponse = await PlaceAlgoOrderAsync(algoParams, apiKey);
                    await DelayAsync(300); // Delay after stop-loss order

                    if (stopLossResponse.IsSuccessful)
                    {
                        Console.WriteLine($"Stop Loss set for {trade.Symbol} at {trade.StopLoss}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to place Stop Loss for {trade.Symbol}. Error: {stopLossResponse.ErrorMessage}");
                        if (!string.IsNullOrEmpty(stopLossResponse.Content))
                        {
                            Console.WriteLine($"Stop loss response content: {stopLossResponse.Content}");
                        }
                    }

                    // Step 3: Either place Take Profit (default) or Trailing Stop (if enabled)
                    serverTime = await GetServerTimeAsync(5005);
                    await DelayAsync(200); // Small delay after timestamp refresh

                    decimal tickSize = GetTickSize(trade.Symbol);
                    decimal roundedTakeProfitPrice = Math.Floor(trade.TakeProfit / tickSize) * tickSize;
                    string takeProfitPriceString = roundedTakeProfitPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);
                    if (_replaceTakeProfitWithTrailing && !explicitStopLossProvided)
                    {
                        // IMPORTANT: activation override is an ATR multiplier, not a percent.
                        // We already derived activationPercent earlier in PlaceOrderAsync based on ATR/Price.
                        // So here we must use the provided derived percent and NOT overwrite with the ATR multiplier.
                        decimal activationPercent = (trailingActivationPercent ?? 1.0m);
                        decimal callbackPercent = (_trailingCallbackPercentOverride ?? trailingCallbackPercent ?? 1.0m);
                        // Clamp callback to Binance constraints 0.1 - 5.0
                        callbackPercent = Math.Min(5.0m, Math.Max(0.1m, callbackPercent));

                        await PlaceTrailingStopLossAsync(trade, activationPercent, callbackPercent, formattedQuantity, apiKey);
                    }
                    else
                    {
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
                        await DelayAsync(300); // Delay after take-profit order

                        if (takeProfitResponse.IsSuccessful)
                        {
                            Console.WriteLine($"Take Profit set for {trade.Symbol} at {roundedTakeProfitPrice}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to place Take Profit for {trade.Symbol}. Error: {takeProfitResponse.ErrorMessage}");
                            if (!string.IsNullOrEmpty(takeProfitResponse.Content))
                            {
                                Console.WriteLine($"Take profit response content: {takeProfitResponse.Content}");
                            }
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Failed to place {orderType} Order for {trade.Symbol}. Error: {response.ErrorMessage}");
                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        Console.WriteLine($"Market order response content: {response.Content}");
                    }
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
            trailingStopLossRequest.AddParameter("reduceOnly", "true");

            // Compute direction-aware activation price
            decimal actPct = Math.Abs(trailingActivationPercent ?? 1.0m);
            decimal activationPrice = trade.IsLong
                ? trade.EntryPrice * (1 + (actPct / 100))
                : trade.EntryPrice * (1 - (actPct / 100));
            string activationPriceString = activationPrice.ToString($"F{pricePrecision}", CultureInfo.InvariantCulture);

            // Clamp callbackRate to [0.1, 5.0] and format with one decimal
            decimal cbPct = Math.Min(5.0m, Math.Max(0.1m, Math.Abs(trailingCallbackPercent ?? 1.0m)));
            string callbackRateString = cbPct.ToString("F1", CultureInfo.InvariantCulture);
            
            trailingStopLossRequest.AddParameter("activationPrice", activationPriceString);
            trailingStopLossRequest.AddParameter("callbackRate", callbackRateString);
            //trailingStopLossRequest.AddParameter("closePosition", "TRUE");
            trailingStopLossRequest.AddParameter("timestamp", serverTime);

            // Send trailing stop as an Algo order (TRAILING_STOP_MARKET)
            var trailingParams = new Dictionary<string, string>
            {
                { "symbol", trade.Symbol },
                { "side", trade.IsLong ? "SELL" : "BUY" },
                { "type", "TRAILING_STOP_MARKET" },
                { "algoType", "CONDITIONAL" },
                { "quantity", formattedQuantity },
                { "reduceOnly", "true" },
                { "activationPrice", activationPriceString },
                { "callbackRate", callbackRateString }
            };

            var stopLossResponse = await PlaceAlgoOrderAsync(trailingParams, apiKey);

            if (stopLossResponse.IsSuccessful)
            {
                Console.WriteLine($"Trailing Stop placed for {trade.Symbol}: activation {activationPriceString}, callback {callbackRateString}% (reduceOnly)");
                try
                {
                    _logger.LogInformation(
                        "[TrailingOrder] {Symbol} {Side} entry={Entry:F6} act%={ActPct:F4}% actPrice={ActPrice} cb%={CbPct:F2}",
                        trade.Symbol,
                        trade.IsLong ? "LONG" : "SHORT",
                        trade.EntryPrice,
                        actPct,
                        activationPriceString,
                        cbPct
                    );
                }
                catch { }
            }
            else
            {
                Console.WriteLine($"Failed to place trailing stop for {trade.Symbol}. Error: {stopLossResponse.ErrorMessage}");
                Console.WriteLine($"Response Content: {stopLossResponse.Content}");
                try
                {
                    _logger.LogWarning(
                        "[TrailingOrderFail] {Symbol} {Side} act%={ActPct:F4}% cb%={CbPct:F2} status={Status} error={Error}",
                        trade.Symbol,
                        trade.IsLong ? "LONG" : "SHORT",
                        actPct,
                        cbPct,
                        stopLossResponse.StatusCode,
                        stopLossResponse.ErrorMessage
                    );
                }
                catch { }
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
                if (!string.IsNullOrEmpty(response.Content))
                {
                    Log.Error($"Response content: {response.Content}");
                }
                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    return false;
                }

                var openPositions = JsonConvert.DeserializeObject<List<PositionRisk>>(response.Content);
                if (openPositions == null) return false;
                var openPosition = openPositions.FirstOrDefault(pos => pos.Symbol == symbol && pos.PositionAmt != 0);
                return openPosition != null;
            }
            catch (Exception ex)
            {
                Log.Error($"Error checking open position: {ex.Message}");
                if (!string.IsNullOrEmpty(response.Content))
                {
                    Log.Error($"Response content: {response.Content}");
                }
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

        private (decimal liquidationPrice, decimal maintenanceMarginRate) CalculateLiquidationPrice(
            string symbol, 
            decimal entryPrice, 
            decimal quantity, 
            bool isLong, 
            decimal leverage)
        {
            // Maintenance margin rate for Binance futures (typically 0.5% or 0.004 for most symbols)
            // You might want to fetch this from exchange info or set a default
            decimal maintenanceMarginRate = 0.004m; // 0.4%
            
            decimal liquidationPrice;
            if (isLong)
            {
                // Long position liquidation price
                liquidationPrice = entryPrice * (1 - (1 / leverage) + maintenanceMarginRate);
            }
            else
            {
                // Short position liquidation price
                liquidationPrice = entryPrice * (1 + (1 / leverage) - maintenanceMarginRate);
            }
            
            return (liquidationPrice, maintenanceMarginRate);
        }

        private async Task DelayAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
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

        private class AlgoOrderResult
        {
            public bool IsSuccessful { get; set; }
            public string Content { get; set; }
            public string ErrorMessage { get; set; }
            public object StatusCode { get; set; }
        }

        private async Task<AlgoOrderResult> PlaceAlgoOrderAsync(Dictionary<string, string> parameters, string apiKey)
        {
            try
            {
                // Ensure timestamp
                if (!parameters.ContainsKey("timestamp"))
                {
                    parameters["timestamp"] = (await GetServerTimeAsync()).ToString();
                }

                // Build a URL-encoded query string in the same order as the parameters dictionary
                var encodedPairs = parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value ?? string.Empty)}");
                var queryString = string.Join("&", encodedPairs);
                var signature = GenerateSignature(queryString);

                // Send the signed query as part of the request URI to avoid mismatched body encoding
                var requestUri = $"/fapi/v1/algoOrder?{queryString}&signature={signature}";
                Console.WriteLine($"[DEBUG] AlgoOrder requestUri: {requestUri}");
                var request = new RestRequest(requestUri, Method.Post);
                request.AddHeader("X-MBX-APIKEY", apiKey);

                var response = await _client.ExecuteAsync(request);

                var result = new AlgoOrderResult
                {
                    IsSuccessful = response.IsSuccessful,
                    Content = response.Content,
                    ErrorMessage = response.ErrorMessage,
                    StatusCode = response.StatusCode
                };

                // Retry once with alternative algoType mapping if exchange reports invalid algoType (-4500)
                if (!result.IsSuccessful && !string.IsNullOrEmpty(result.Content) && result.Content.Contains("Invalid algoType"))
                {
                    try
                    {
                        Console.WriteLine("[DEBUG] Invalid algoType reported by exchange; attempting alternate algoType value and retrying.");
                        if (parameters.ContainsKey("algoType"))
                        {
                            var orig = parameters["algoType"] ?? string.Empty;
                            string alt;
                            if (orig.EndsWith("_MARKET", StringComparison.OrdinalIgnoreCase))
                                alt = orig.Substring(0, orig.Length - 7); // strip _MARKET
                            else
                                alt = orig + "_MARKET"; // append

                            parameters["algoType"] = alt;
                            var encodedPairs2 = parameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value ?? string.Empty)}");
                            var queryString2 = string.Join("&", encodedPairs2);
                            var signature2 = GenerateSignature(queryString2);
                            var requestUri2 = $"/fapi/v1/algoOrder?{queryString2}&signature={signature2}";
                            Console.WriteLine($"[DEBUG] Retrying AlgoOrder requestUri: {requestUri2}");
                            var request2 = new RestRequest(requestUri2, Method.Post);
                            request2.AddHeader("X-MBX-APIKEY", apiKey);
                            var response2 = await _client.ExecuteAsync(request2);
                            return new AlgoOrderResult
                            {
                                IsSuccessful = response2.IsSuccessful,
                                Content = response2.Content,
                                ErrorMessage = response2.ErrorMessage,
                                StatusCode = response2.StatusCode
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DEBUG] Retry for algoType failed: {ex.Message}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new AlgoOrderResult
                {
                    IsSuccessful = false,
                    Content = ex.ToString(),
                    ErrorMessage = ex.Message,
                    StatusCode = null
                };
            }
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