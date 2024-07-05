using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using Serilog;

public class OrderManager
{
    private readonly Wallet _wallet;
    private readonly Dictionary<string, Trade> _activeTrades = new Dictionary<string, Trade>();
    private readonly decimal _leverage;
    public decimal noOfTrades = 0;
    public decimal profitOfClosed = 0;
    public decimal longs = 0;
    public decimal shorts = 0;
    private List<TradeRecord> tradeRecords = new List<TradeRecord>();

    private readonly string _excelFilePath;

    public OrderManager(Wallet wallet, decimal leverage, string excelFilePath)
    {
        _wallet = wallet;
        _leverage = leverage;
        _excelFilePath = excelFilePath;
    }

    private decimal CalculateQuantity(decimal price)
    {
        decimal marginPerTrade = 20; // Fixed margin per trade in USDT
        decimal quantity = (marginPerTrade * _leverage) / price;
        return quantity;
    }

    public void PlaceLongOrder(string symbol, decimal price, string signalIn)
    {
        PlaceOrder(symbol, price, true, signalIn);
    }

    public void PlaceShortOrder(string symbol, decimal price, string signalIn)
    {
        PlaceOrder(symbol, price, false, signalIn);
    }

    private void PlaceOrder(string symbol, decimal price, bool isLong, string signalIn)
    {
        decimal quantity = CalculateQuantity(price);

        if (_activeTrades.Count >= 25)
        {
            return;
        }

        if (_activeTrades.ContainsKey(symbol))
        {
            return;
        }

        decimal takeProfitPrice;
        decimal stopLossPrice;

        if (isLong)
        {
            takeProfitPrice = price * 1.005m; // 0.5% take profit above entry price
            stopLossPrice = price * 0.9975m; // 0.25% stop loss below entry price
        }
        else
        {
            takeProfitPrice = price * 0.9975m; // 0.25% take profit below entry price
            stopLossPrice = price * 1.005m; // 0.5% stop loss above entry price
        }

        var trade = new Trade(symbol, price, takeProfitPrice, stopLossPrice, quantity, isLong, _leverage);

        if (_wallet.PlaceTrade(trade))
        {
            if (trade.IsLong) longs++;
            else shorts++;
            noOfTrades++;
            _activeTrades[symbol] = trade;
            RecordTrade(symbol, price, 0, isLong, quantity, signalIn, 0); // Initial record, exit price, and profit will be updated later
        }
    }

    public void CheckAndCloseTrades(Dictionary<string, decimal> currentPrices)
    {
        foreach (var trade in _activeTrades.Values.ToList())
        {
            if (currentPrices != null && trade != null &&
                currentPrices.ContainsKey(trade.Symbol) &&
                ShouldCloseTrade(trade, currentPrices[trade.Symbol]))
            {
                var closingPrice = currentPrices[trade.Symbol];
                var realizedReturn = trade.CalculateRealizedReturn(closingPrice);
                var profit = trade.IsLong
                    ? (trade.Quantity * (closingPrice - trade.EntryPrice)) + trade.InitialMargin
                    : (trade.Quantity * (trade.EntryPrice - closingPrice)) + trade.InitialMargin;

                if (trade.IsLong)
                {
                    _wallet.AddFunds((trade.Quantity * (closingPrice - trade.EntryPrice)) + trade.InitialMargin);
                }
                else
                {
                    _wallet.AddFunds((trade.Quantity * (trade.EntryPrice - closingPrice)) + trade.InitialMargin);
                }

                _activeTrades.Remove(trade.Symbol);

                RecordTrade(trade.Symbol, trade.EntryPrice, closingPrice, trade.IsLong, trade.Quantity, "Closed", profit - trade.InitialMargin);

                Console.WriteLine($"Trade for {trade.Symbol} closed.");
                Console.WriteLine($"Realized Return for {trade.Symbol}: {realizedReturn:P2}");
            }
        }
    }

    private bool ShouldCloseTrade(Trade trade, decimal currentPrice)
    {
        if (trade.IsLong)
        {
            return currentPrice >= trade.TakeProfitPrice || currentPrice <= trade.StopLossPrice;
        }
        else
        {
            return currentPrice <= trade.TakeProfitPrice || currentPrice >= trade.StopLossPrice;
        }
    }

    private void RecordTrade(string symbol, decimal entryPrice, decimal exitPrice, bool isLong, decimal quantity, string signal, decimal profit)
    {
        var record = new TradeRecord
        {
            Symbol = symbol,
            EntryPrice = entryPrice,
            ExitPrice = exitPrice,
            IsLong = isLong,
            Quantity = quantity,
            Signal = signal,
            Profit = profit,
            Timestamp = DateTime.Now
        };
        tradeRecords.Add(record);
    }

    public void PrintActiveTrades(Dictionary<string, decimal> currentPrices)
    {
        Console.WriteLine("Active Trades:");
        string logOutput = "Active Trades:\n";

        foreach (var trade in _activeTrades.Values)
        {
            decimal leverageMultiplier = trade.Leverage;
            decimal initialValue = trade.InitialMargin * leverageMultiplier;

            decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
            decimal entryValue = trade.Quantity * trade.EntryPrice;

            decimal realizedReturn = ((currentValue - initialValue) / initialValue) * 100;

            Console.WriteLine($"{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2} USDT, Quantity: {trade.Quantity:F8},");
            Console.WriteLine($"Entry Price: {trade.EntryPrice:F8}, Current Price: {currentPrices[trade.Symbol]:F8},");
            Console.WriteLine($"Take Profit: {trade.TakeProfitPrice:F8}, Stop Loss: {trade.StopLossPrice:F8},");
            logOutput += $"{trade.Symbol}: Initial Margin: {trade.InitialMargin:F2} USDT, Quantity: {trade.Quantity:F8},\n";
            logOutput += $"Entry Price: {trade.EntryPrice:F8}, Current Price: {currentPrices[trade.Symbol]:F8},\n";
            logOutput += $"Take Profit: {trade.TakeProfitPrice:F8}, Stop Loss: {trade.StopLossPrice:F8},\n";

            if (trade.IsLong)
            {
                decimal valueOfTrade = currentValue - entryValue;
                Console.WriteLine($"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,");
                logOutput += $"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,\n";
            }
            else // SHORT trade
            {
                decimal valueOfTrade = entryValue - currentValue;
                realizedReturn = ((initialValue - currentValue) / initialValue) * 100;
                Console.WriteLine($"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,");
                logOutput += $"Value Of Trade: {valueOfTrade:F2} USDT, Realized Return: {realizedReturn:F2}%,\n";
            }

            var tradeRecord = tradeRecords.FirstOrDefault(record => record.Symbol == trade.Symbol && record.EntryPrice == trade.EntryPrice);
            if (tradeRecord != null)
            {
                Console.WriteLine($"Signal: {tradeRecord.Signal}");
                logOutput += $"Signal: {tradeRecord.Signal}\n";
            }

            Console.WriteLine($"Direction: {(trade.IsLong ? "Long" : "Short")}, Leverage: {leverageMultiplier}x\n");
            logOutput += $"Direction: {(trade.IsLong ? "Long" : "Short")}, Leverage: {leverageMultiplier}x\n\n";
        }

        decimal totalValue = _activeTrades.Values.Sum(trade =>
        {
            decimal leverageMultiplier = trade.Leverage;
            decimal initialValue = trade.InitialMargin * leverageMultiplier;
            decimal currentValue = trade.Quantity * currentPrices[trade.Symbol];
            decimal entryValue = trade.Quantity * trade.EntryPrice;

            if (trade.IsLong)
            {
                return (currentValue - initialValue) - (entryValue - initialValue);
            }
            else // SHORT trade
            {
                return (entryValue - initialValue) - (currentValue - initialValue);
            }
        });

        string totalOutput = $"Total Value of Active Trades: {totalValue + (_activeTrades.Count * 20)} USDT \nProfit of Closed Trades: {profitOfClosed}\nLongs: {longs}\nShorts: {shorts}";
        Console.WriteLine(totalOutput);
        Log.Information(logOutput);
        Log.Information(totalOutput);
        Log.Information($"Number of trades: {noOfTrades}");
    }

    public void PrintWalletBalance()
    {
        Console.WriteLine($"Wallet Balance: {_wallet.GetBalance():F2} USDT");
    }
public void WriteTradesToExcel(string filePath)
    {
        try
        {
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Trades");

                worksheet.Cells[1, 1].Value = "Symbol";
                worksheet.Cells[1, 2].Value = "EntryPrice";
                worksheet.Cells[1, 3].Value = "ExitPrice";
                worksheet.Cells[1, 4].Value = "IsLong";
                worksheet.Cells[1, 5].Value = "Quantity";
                worksheet.Cells[1, 6].Value = "Signal";
                worksheet.Cells[1, 7].Value = "Profit";
                worksheet.Cells[1, 8].Value = "Timestamp";

                for (int i = 0; i < tradeRecords.Count; i++)
                {
                    var record = tradeRecords[i];
                    worksheet.Cells[i + 2, 1].Value = record.Symbol;
                    worksheet.Cells[i + 2, 2].Value = record.EntryPrice;
                    worksheet.Cells[i + 2, 3].Value = record.ExitPrice;
                    worksheet.Cells[i + 2, 4].Value = record.IsLong;
                    worksheet.Cells[i + 2, 5].Value = record.Quantity;
                    worksheet.Cells[i + 2, 6].Value = record.Signal;
                    worksheet.Cells[i + 2, 7].Value = record.Profit;
                    worksheet.Cells[i + 2, 8].Value = record.Timestamp;
                    worksheet.Cells[i + 2, 8].Style.Numberformat.Format = "yyyy-mm-dd hh:mm:ss"; // Format the timestamp column
                }

                package.SaveAs(new FileInfo(filePath));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write trades to Excel file: {ex.Message}");
            Log.Error($"Failed to write trades to Excel file: {ex.Message}");
        }
    }
}
