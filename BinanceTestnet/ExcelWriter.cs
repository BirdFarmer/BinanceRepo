using OfficeOpenXml;
using System;
using System.IO;
using BinanceTestnet.Trading;
using System.Collections.Concurrent;
using OfficeOpenXml.Drawing.Chart;

public class ExcelWriter
{
    private readonly string _folderPath;
    private readonly string _filePath;
    private ExcelPackage? _package;
    public string FilePath => _filePath;

    public ExcelWriter(string folderPath = "C:\\Repo\\BinanceAPI\\BinanceTestnet\\Excels", string fileName = "ClosedTrades.xlsx")
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentNullException(nameof(folderPath), "Folder path cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentNullException(nameof(fileName), "File name cannot be null or empty.");
        }

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        _folderPath = folderPath;
        _filePath = Path.Combine(_folderPath, fileName);

        if (!Directory.Exists(_folderPath))
        {
            try
            {
                Directory.CreateDirectory(_folderPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Failed to create directory {_folderPath}.", ex);
            }
        }
    }

    public void Initialize(string fileName)
    {
        var fileInfo = new FileInfo(fileName);
        
        // Open the Excel file with shared read access
        using (FileStream stream = new FileStream(fileInfo.FullName, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
        {
            _package = new ExcelPackage(stream);
        }

        Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCancelKeyPress);
    }


    private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        _package.Dispose();
    }

    public void WriteClosedTradeToExcel(Trade trade, decimal takeProfit, decimal tpIteration, ConcurrentDictionary<int, Trade> activeTrades, string interval)
    {
        try
        {
            using (var package = new ExcelPackage(new FileInfo(_filePath)))
            {
                bool fileExists = File.Exists(_filePath);
            
                _package = new ExcelPackage(new FileInfo(_filePath));
            
                string sheetName = "Active Trades";
                ExcelWorksheet worksheet = _package.Workbook.Worksheets[sheetName];
            
                if (worksheet != null)
                {
                    RewriteActiveTradesSheet(activeTrades.Values.ToList(), tpIteration);
                }

                sheetName = $"TP_{tpIteration}:TF_{interval}";
                worksheet = _package.Workbook.Worksheets[sheetName] ?? _package.Workbook.Worksheets.Add(sheetName);

                if (string.IsNullOrEmpty(worksheet.Cells[1, 1].Text) || worksheet.Name != sheetName)
                {
                    worksheet.Cells[1, 1].Value = "Id";
                    worksheet.Cells[1, 2].Value = "Symbol";
                    worksheet.Cells[1, 3].Value = "Leverage";
                    worksheet.Cells[1, 4].Value = "IsLong";
                    worksheet.Cells[1, 5].Value = "Signal";
                    worksheet.Cells[1, 6].Value = "Entry time";  // Updated to use Kline timestamp
                    worksheet.Cells[1, 7].Value = "Duration";
                    worksheet.Cells[1, 8].Value = "Profit";
                    worksheet.Cells[1, 9].Value = "Funds Added";
                    worksheet.Cells[1, 10].Value = "Entry Price";
                    worksheet.Cells[1, 11].Value = "TP Price";
                    worksheet.Cells[1, 12].Value = "SL Price";
                    worksheet.Cells[1, 13].Value = "Exit time";
                    worksheet.Column(26).Hidden = true;
                }

                int lastTradeRow = worksheet.Dimension?.End.Row ?? 1;
                for (int i = 1; i <= lastTradeRow; i++)
                {
                    if (string.IsNullOrEmpty(worksheet.Cells[i, 1].Text))
                    {
                        lastTradeRow = i - 1;
                        break;
                    }
                }
                int nextTradeRow = lastTradeRow + 1;

                decimal fundsAdded = trade.Profit.HasValue ? trade.Profit.Value * trade.InitialMargin : 0;

                worksheet.Cells[nextTradeRow, 1].Value = trade.Id;
                worksheet.Cells[nextTradeRow, 2].Value = trade.Symbol;
                worksheet.Cells[nextTradeRow, 3].Value = trade.Leverage;
                worksheet.Cells[nextTradeRow, 4].Value = trade.IsLong ? "Long" : "Short";
                worksheet.Cells[nextTradeRow, 5].Value = trade.Signal;
                worksheet.Cells[nextTradeRow, 6].Value = TimeZoneInfo.ConvertTimeFromUtc(trade.KlineTimestamp, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).ToString("MM/dd HH:mm"); // Write the Kline timestamp in CET
                worksheet.Cells[nextTradeRow, 7].Value = Math.Round(trade.Duration.TotalMinutes);
                worksheet.Cells[nextTradeRow, 8].Value = trade.Profit.HasValue ? trade.Profit : 0;
                worksheet.Cells[nextTradeRow, 9].Value = fundsAdded;
                worksheet.Cells[nextTradeRow, 10].Value = trade.EntryPrice;
                worksheet.Cells[nextTradeRow, 11].Value = trade.TakeProfitPrice;
                worksheet.Cells[nextTradeRow, 12].Value = trade.StopLossPrice;
                worksheet.Cells[nextTradeRow, 13].Formula = $"=F{nextTradeRow} + TIME(0, G{nextTradeRow}, 0)";

                worksheet.Cells[nextTradeRow, 26].Formula = $"=AVERAGEIFS(I:I, B:B, B{nextTradeRow})";

                worksheet.Cells[5, 17].Value = "Total Funds Added";
                worksheet.Cells[5, 18].Formula = "ROUND(SUM(I:I), 3)";

                worksheet.Cells[6, 17].Value = "Total Closed Trades";
                worksheet.Cells[6, 18].Formula = "COUNTA(A:A) - 1";

                worksheet.Cells[7, 17].Value = "Average Duration hh:mm:ss";
                worksheet.Cells[7, 18].Formula = "IFERROR(TEXT(AVERAGE(G:G)/1440, \"[h]:mm:ss\"), \"N/A\")";

                worksheet.Cells[8, 17].Value = "Longs Average Profit";
                worksheet.Cells[8, 18].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Long\", H:H), 3), \"N/A\")";

                worksheet.Cells[9, 17].Value = "Shorts Average Profit";
                worksheet.Cells[9, 18].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Short\", H:H), 3), \"N/A\")";

                worksheet.Cells[10, 17].Value = "MAC-D Average Profit";
                worksheet.Cells[10, 18].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"Enhanced MACD\", H:H), 3), \"N/A\")";

                worksheet.Cells[11, 17].Value = "SMA Average Profit";
                worksheet.Cells[11, 18].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"SMAExpansion\", H:H), 3), \"N/A\")";

                worksheet.Cells[12, 17].Value = "Hull SMA Average Profit";
                worksheet.Cells[12, 18].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"Hull SMA\", H:H), 3), \"N/A\")";

                worksheet.Cells[13, 17].Value = "SMA Long Combined Average";
                worksheet.Cells[13, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

                worksheet.Cells[14, 17].Value = "SMA Short Combined Average";
                worksheet.Cells[14, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

                worksheet.Cells[15, 17].Value = "MAC-D Long Combined Average";
                worksheet.Cells[15, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"Enhanced MACD\"), 3), \"N/A\")";

                worksheet.Cells[16, 17].Value = "MAC-D Short Combined Average";
                worksheet.Cells[16, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"Enhanced MACD\"), 3), \"N/A\")";

                worksheet.Cells[17, 17].Value = "Hull SMA Long Combined Average";
                worksheet.Cells[17, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"Hull SMA\"), 3), \"N/A\")";

                worksheet.Cells[18, 17].Value = "Hull SMA Short Combined Average";
                worksheet.Cells[18, 18].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"Hull SMA\"), 3), \"N/A\")";

                worksheet.Cells[20, 17].Value = "Win Rate";
                worksheet.Cells[20, 18].Formula = "IFERROR(ROUND(COUNTIF(H:H, \">0\") / COUNT(H:H), 3), \"N/A\")";

                worksheet.Cells[21, 17].Value = "Sharpe Ratio";
                worksheet.Cells[21, 18].Formula = "IFERROR(ROUND(AVERAGE(H:H) / STDEV(H:H), 3), \"N/A\")";

                worksheet.Cells[22, 17].Value = "Profit Factor";
                worksheet.Cells[22, 18].Formula = "IFERROR(ROUND(SUMIF(H:H, \">0\") / ABS(SUMIF(H:H, \"<0\")), 3), \"N/A\")";

                worksheet.Cells[24, 17].Value = "Total Long Trades";
                worksheet.Cells[24, 18].Formula = "COUNTIF(D:D, \"Long\")";

                worksheet.Cells[25, 17].Value = "Total Short Trades";
                worksheet.Cells[25, 18].Formula = "COUNTIF(D:D, \"Short\")";

                worksheet.Cells[26, 17].Value = "Maximum Profit per Trade";
                worksheet.Cells[26, 18].Formula = "IFERROR(ROUND(MAX(H:H), 3), \"N/A\")";

                worksheet.Cells[27, 17].Value = "Minimum Profit per Trade";
                worksheet.Cells[27, 18].Formula = "IFERROR(ROUND(MIN(H:H), 3), \"N/A\")";

                worksheet.Cells[29, 17].Value = "Best Coin";
                worksheet.Cells[29, 18].Formula = "=IFERROR(INDEX(B2:B1000, MATCH(MAX(Z2:Z1000), Z2:Z1000, 0)) & \" \" & TEXT(MAX(Z2:Z1000), \"#.00\"), \"N/A\")";

                worksheet.Cells[30, 17].Value = "Worst Coin";
                worksheet.Cells[30, 18].Formula = "=IFERROR(INDEX(B2:B1000, MATCH(MIN(Z2:Z1000), Z2:Z1000, 0)) & \" \" & TEXT(MIN(Z2:Z1000), \"#.00\"), \"N/A\")";

                // Save the package
                _package.Save();    
            }
        }
        catch (InvalidOperationException ex)
        {
            // Log the exception and move on
            Console.WriteLine($"Error saving file: {ex.Message}");
            // Optionally log more details or retry
        }
        catch (Exception ex)
        {
            // Catch any other exceptions and handle them appropriately
            Console.WriteLine($"Unexpected error occurred: {ex.Message}");
        }
    }

    public void RewriteActiveTradesSheet(List<Trade> activeTrades, decimal setTP)
    {
        try
        {
            bool fileExists = File.Exists(_filePath);

            _package = new ExcelPackage(new FileInfo(_filePath));

            string sheetName = "Active Trades";
            ExcelWorksheet worksheet = _package.Workbook.Worksheets[sheetName] ?? _package.Workbook.Worksheets.Add(sheetName);

            // Clear the ActiveTrades sheet
            worksheet.Cells.Clear();

            // Set the headers
            worksheet.Cells[1, 1].Value = "Id";
            worksheet.Cells[1, 2].Value = "Symbol";
            worksheet.Cells[1, 3].Value = "Trade Type";        
            worksheet.Cells[1, 4].Value = "Signal";
            worksheet.Cells[1, 5].Value = "Kline Timestamp";
            worksheet.Cells[1, 6].Value = "Entry Price";        
            worksheet.Cells[1, 7].Value = "Take Profit Price";
            worksheet.Cells[1, 8].Value = "Stop Loss Price";
            worksheet.Cells[1, 9].Value = "Expected Profit excl. leverage";
            worksheet.Cells[1, 10].Value = "Set TP %";

            // Write the active trades to the sheet
            int nextTradeRow = 2;
            foreach (var trade in activeTrades)
            {
                worksheet.Cells[nextTradeRow, 1].Value = trade.Id;
                worksheet.Cells[nextTradeRow, 2].Value = trade.Symbol;
                worksheet.Cells[nextTradeRow, 3].Value = trade.IsLong ? "Long" : "Short";
                worksheet.Cells[nextTradeRow, 4].Value = trade.Signal;
                worksheet.Cells[nextTradeRow, 5].Value = TimeZoneInfo.ConvertTimeFromUtc(trade.KlineTimestamp, TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time")).ToString("MM/dd HH:mm"); // Write the Kline timestamp in CET
                worksheet.Cells[nextTradeRow, 6].Value = trade.EntryPrice;
                worksheet.Cells[nextTradeRow, 7].Value = trade.TakeProfitPrice;
                worksheet.Cells[nextTradeRow, 8].Value = trade.StopLossPrice;
                worksheet.Cells[nextTradeRow, 9].Formula = "=IF(C" + nextTradeRow + "= \"Long\", (G" + nextTradeRow + "-F" + nextTradeRow + ")/F" + nextTradeRow + ", (F" + nextTradeRow + "-G" + nextTradeRow + ")/F" + nextTradeRow + ") * 100";
                worksheet.Cells[nextTradeRow, 10].Value = setTP;
                nextTradeRow++;
            }

            _package.Save();
        }
        catch (InvalidOperationException ex)
        {
            // Log the exception and move on
            Console.WriteLine($"Error saving file: {ex.Message}");
            // Optionally log more details or retry
        }
        catch (Exception ex)
        {
            // Catch any other exceptions and handle them appropriately
            Console.WriteLine($"Unexpected error occurred: {ex.Message}");
        }
    }
}

