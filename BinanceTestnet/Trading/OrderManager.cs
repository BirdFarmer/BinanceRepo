using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BinanceTestnet.Models;
using BinanceLive.Services;
using BinanceTestnet.Enums;
using RestSharp;
using Skender.Stock.Indicators;

namespace BinanceTestnet.Trading
{
    public class OrderManager
    {
        public Wallet _wallet;
        private readonly ConcurrentDictionary<int, Trade> _activeTrades = new ConcurrentDictionary<int, Trade>();
        public decimal _leverage;
        public string _interval;
        private readonly ExcelWriter _excelWriter;
        private readonly OperationMode _operationMode;
        private decimal noOfTrades = 0;
        private decimal profitOfClosed = 0;
        private decimal longs = 0;
        private decimal shorts = 0;
        private int _nextTradeId = 1;
        private decimal _takeProfit;
        private decimal _tpIteration;
        private readonly SelectedTradeDirection _tradeDirection;
        private readonly SelectedTradingStrategy _tradingStrategy;
        private readonly RestClient _client;

        public OrderManager(Wallet wallet, decimal leverage, ExcelWriter excelWriter, OperationMode operationMode,
                            string interval, string fileName, decimal takeProfit,
                            SelectedTradeDirection tradeDirection, SelectedTradingStrategy tradingStrategy,
                            RestClient client, decimal tpIteration)
        {
            _wallet = wallet;
            _leverage = leverage;
            _excelWriter = excelWriter;
            _operationMode = operationMode;
            _interval = interval;
            _takeProfit = takeProfit;
            _tpIteration = tpIteration;
            _excelWriter.Initialize(fileName);
            _tradeDirection = tradeDirection;
            _tradingStrategy = tradingStrategy;
            _client = client;
        }

        private decimal CalculateQuantity(decimal price)
        {
            decimal marginPerTrade = 20;
            decimal quantity = (marginPerTrade * _leverage) / price;
            return quantity;
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

                if (_activeTrades.Count >= 14)
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

            decimal quantity = CalculateQuantity(price);

            var trade = new Trade(_nextTradeId++, symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage, signal, _interval, timestampEntry);

            lock (_activeTrades) // Lock to ensure thread safety
            {
                if (_wallet.PlaceTrade(trade))
                {
                    Console.Beep();
                    Console.WriteLine($"With signal: {signal}");
                    if (trade.IsLong) longs++;
                    else shorts++;
                    noOfTrades++;
                    _activeTrades[trade.Id] = trade;
                    _excelWriter.RewriteActiveTradesSheet(_activeTrades.Values.ToList());
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

            return VolatilityBasedTPandSL.CalculateTpAndSl(symbol, symbolQuotes, btcQuotes, _takeProfit);
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

                    _excelWriter.WriteClosedTradeToExcel(trade, _takeProfit, _tpIteration, _activeTrades);
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

                    _excelWriter.WriteClosedTradeToExcel(trade, _takeProfit, _tpIteration, _activeTrades);
                }
            }
        }

        public void UpdateSettings(decimal leverage, string interval, decimal takeProfit)
        {
            _leverage = leverage;
            _interval = interval;
            _takeProfit = takeProfit;
        }

        public void UpdateParams(Wallet wallet, decimal tpPercent)
        {
            _takeProfit = tpPercent;
            _wallet = wallet;
        }

    }
}