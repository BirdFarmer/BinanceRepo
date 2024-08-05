using OfficeOpenXml;
using System;
using System.IO;

public class ExcelWriter
{
    private readonly string _folderPath;
    private readonly string _filePath;
    private ExcelPackage _package;

    public ExcelWriter(string folderPath = "C:\\Repo\\BinanceAPI\\BinanceTestnet\\Excels", string fileName = "ClosedTrades.xlsx")
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        _folderPath = folderPath;
        _filePath = Path.Combine(_folderPath, fileName);

        if (!Directory.Exists(_folderPath))
        {
            Directory.CreateDirectory(_folderPath);
        }
    }

    public void Initialize(string fileName)
    {
        _package = new ExcelPackage(new FileInfo(fileName));
    }

    public void WriteClosedTradeToExcel(Trade trade, decimal takeProfit)
    {
        bool fileExists = File.Exists(_filePath);

        using (var package = new ExcelPackage(new FileInfo(_filePath)))
        {
            ExcelWorksheet worksheet = fileExists ? package.Workbook.Worksheets[0] : package.Workbook.Worksheets.Add("Closed Trades");

            if (!fileExists)
            {
                worksheet.Cells[1, 1].Value = "Id";
                worksheet.Cells[1, 2].Value = "Symbol";
                worksheet.Cells[1, 3].Value = "Leverage";
                worksheet.Cells[1, 4].Value = "IsLong";
                worksheet.Cells[1, 5].Value = "Signal";
                worksheet.Cells[1, 6].Value = "EntryTimestamp";
                worksheet.Cells[1, 7].Value = "Duration";
                worksheet.Cells[1, 8].Value = "Profit";
                worksheet.Cells[1, 9].Value = "Funds Added";
                worksheet.Cells[1, 10].Value = "US Market Hours";
                worksheet.Cells[1, 11].Value = "TP%";
                worksheet.Cells[1, 12].Value = "Adjusted TP";
            }

            // Find the next available row for trade data
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
            decimal adjustedTP = takeProfit / trade.Leverage;

            worksheet.Cells[nextTradeRow, 1].Value = trade.Id;
            worksheet.Cells[nextTradeRow, 2].Value = trade.Symbol;
            worksheet.Cells[nextTradeRow, 3].Value = trade.Leverage;
            worksheet.Cells[nextTradeRow, 4].Value = trade.IsLong ? "Long" : "Short";
            worksheet.Cells[nextTradeRow, 5].Value = trade.Signal;
            worksheet.Cells[nextTradeRow, 6].Value = trade.EntryTimestamp.ToString("MM/dd HH:mm");
            worksheet.Cells[nextTradeRow, 7].Value = trade.Duration.TotalMinutes;
            worksheet.Cells[nextTradeRow, 8].Value = trade.Profit.HasValue ? trade.Profit : 0;
            worksheet.Cells[nextTradeRow, 9].Value = fundsAdded;

            bool isUSMarketHours = trade.EntryTimestamp.TimeOfDay >= new TimeSpan(14, 30, 0) && trade.EntryTimestamp.TimeOfDay <= new TimeSpan(21, 0, 0);
            worksheet.Cells[nextTradeRow, 10].Value = isUSMarketHours ? "Yes" : "No";
            worksheet.Cells[nextTradeRow, 11].Value = takeProfit;
            worksheet.Cells[nextTradeRow, 12].Value = adjustedTP;

            // Update dashboard statistics (shifted to the right)
            worksheet.Cells[5, 14].Value = "Total Funds Added";
            worksheet.Cells[5, 15].Formula = "ROUND(SUM(I:I), 3)";

            worksheet.Cells[6, 14].Value = "Total Closed Trades";
            worksheet.Cells[6, 15].Formula = "COUNTA(A:A) - 1";

            worksheet.Cells[7, 14].Value = "Average Duration hh:mm:ss";
            worksheet.Cells[7, 15].Formula = "IFERROR(TEXT(AVERAGE(G:G)/1440, \"[h]:mm:ss\"), \"N/A\")";

            worksheet.Cells[8, 14].Value = "Longs Average Profit";
            worksheet.Cells[8, 15].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Long\", H:H), 3), \"N/A\")";

            worksheet.Cells[9, 14].Value = "Shorts Average Profit";
            worksheet.Cells[9, 15].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Short\", H:H), 3), \"N/A\")";

            worksheet.Cells[10, 14].Value = "MAC-D Average Profit";
            worksheet.Cells[10, 15].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"MAC-D\", H:H), 3), \"N/A\")";

            worksheet.Cells[11, 14].Value = "SMA Average Profit";
            worksheet.Cells[11, 15].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"SMAExpansion\", H:H), 3), \"N/A\")";

            worksheet.Cells[12, 14].Value = "Aroon Average Profit";
            worksheet.Cells[12, 15].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"Aroon\", H:H), 3), \"N/A\")";

            worksheet.Cells[13, 14].Value = "SMA Short Combined Average";
            worksheet.Cells[13, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[14, 14].Value = "SMA Long Combined Average";
            worksheet.Cells[14, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[15, 14].Value = "MAC-D Long Combined Average";
            worksheet.Cells[15, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[16, 14].Value = "MAC-D Short Combined Average";
            worksheet.Cells[16, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[17, 14].Value = "Aroon Long Combined Average";
            worksheet.Cells[17, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Long\", E:E, \"Aroon\"), 3), \"N/A\")";

            worksheet.Cells[18, 14].Value = "Aroon Short Combined Average";
            worksheet.Cells[18, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, D:D, \"Short\", E:E, \"Aroon\"), 3), \"N/A\")";

            worksheet.Cells[20, 14].Value = "Win Rate";
            worksheet.Cells[20, 15].Formula = "IFERROR(ROUND(COUNTIF(H:H, \">0\") / COUNT(H:H), 3), \"N/A\")";

            worksheet.Cells[21, 14].Value = "Sharpe Ratio";
            worksheet.Cells[21, 15].Formula = "IFERROR(ROUND(AVERAGE(H:H) / STDEV(H:H), 3), \"N/A\")";

            worksheet.Cells[22, 14].Value = "Profit Factor";
            worksheet.Cells[22, 15].Formula = "IFERROR(ROUND(SUMIF(H:H, \">0\") / ABS(SUMIF(H:H, \"<0\")), 3), \"N/A\")";

            worksheet.Cells[24, 14].Value = "Total Long Trades";
            worksheet.Cells[24, 15].Formula = "COUNTIF(D:D, \"Long\")";

            worksheet.Cells[25, 14].Value = "Total Short Trades";
            worksheet.Cells[25, 15].Formula = "COUNTIF(D:D, \"Short\")";

            worksheet.Cells[26, 14].Value = "Maximum Profit per Trade";
            worksheet.Cells[26, 15].Formula = "IFERROR(ROUND(MAX(H:H), 3), \"N/A\")";

            worksheet.Cells[27, 14].Value = "Maximum Loss per Trade";
            worksheet.Cells[27, 15].Formula = "IFERROR(ROUND(MIN(H:H), 3), \"N/A\")";

            // New statistics for US market hours
            worksheet.Cells[29, 14].Value = "US Market Hours Trades";
            worksheet.Cells[29, 15].Formula = "COUNTIF(J:J, \"Yes\")";

            worksheet.Cells[30, 14].Value = "US Market Hours Profit";
            worksheet.Cells[30, 15].Formula = "IFERROR(ROUND(SUMIFS(H:H, J:J, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[31, 14].Value = "US Market Hours Average Profit";
            worksheet.Cells[31, 15].Formula = "IFERROR(ROUND(AVERAGEIFS(H:H, J:J, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[32, 14].Value = "US Market Hours Win Rate";
            worksheet.Cells[32, 15].Formula = "IFERROR(ROUND(COUNTIFS(H:H, \">0\", J:J, \"Yes\") / COUNTIFS(J:J, \"Yes\"), 3), \"N/A\")";
            
            // Define your take profit levels
            var backtestTakeProfits = new List<decimal> { 0.3M, 0.6M, 0.9M, 1.2M, 1.5M };

            // Add funds added for different take profit levels
            for (int i = 0; i < backtestTakeProfits.Count; i++)
            {
                decimal tpLevel = backtestTakeProfits[i];
                int tpRow = 34 + (int)(tpLevel * 2); // Adjust row calculation as needed
                worksheet.Cells[tpRow, 14].Value = $"Funds added {tpLevel * 100}% TP"; // Percentage format
                worksheet.Cells[tpRow, 15].Formula = $"SUMIFS(I:I, K:K, {tpLevel})"; // Formula for summing funds
            }

            package.Save();
        }
    }
}
