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

    public void WriteClosedTradeToExcel(Trade trade)
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
                worksheet.Cells[1, 4].Value = "Interval";
                worksheet.Cells[1, 5].Value = "IsLong";
                worksheet.Cells[1, 6].Value = "Signal";
                worksheet.Cells[1, 7].Value = "EntryTimestamp";
                worksheet.Cells[1, 8].Value = "Duration";
                worksheet.Cells[1, 9].Value = "Profit";
                worksheet.Cells[1, 10].Value = "Funds Added";
                worksheet.Cells[1, 11].Value = "US Market Hours";
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

            worksheet.Cells[nextTradeRow, 1].Value = trade.Id;
            worksheet.Cells[nextTradeRow, 2].Value = trade.Symbol;
            worksheet.Cells[nextTradeRow, 3].Value = trade.Leverage;
            worksheet.Cells[nextTradeRow, 4].Value = trade.Interval;
            worksheet.Cells[nextTradeRow, 5].Value = trade.IsLong ? "Long" : "Short";
            worksheet.Cells[nextTradeRow, 6].Value = trade.Signal;
            worksheet.Cells[nextTradeRow, 7].Value = trade.EntryTimestamp.ToString("MM/dd HH:mm");
            worksheet.Cells[nextTradeRow, 8].Value = trade.Duration.TotalMinutes;
            worksheet.Cells[nextTradeRow, 9].Value = trade.Profit.HasValue ? trade.Profit : 0;
            worksheet.Cells[nextTradeRow, 10].Value = fundsAdded;

            bool isUSMarketHours = trade.EntryTimestamp.TimeOfDay >= new TimeSpan(14, 30, 0) && trade.EntryTimestamp.TimeOfDay <= new TimeSpan(21, 0, 0);
            worksheet.Cells[nextTradeRow, 11].Value = isUSMarketHours ? "Yes" : "No";

            // Update dashboard statistics (shifted to the right)
            worksheet.Cells[5, 15].Value = "Total Funds Added";
            worksheet.Cells[5, 16].Formula = "ROUND(SUM(J:J), 3)";

            worksheet.Cells[6, 15].Value = "Total Closed Trades";
            worksheet.Cells[6, 16].Formula = "COUNTA(A:A) - 1";

            worksheet.Cells[7, 15].Value = "Average Duration hh:mm:ss";
            worksheet.Cells[7, 16].Formula = "IFERROR(TEXT(AVERAGE(H:H)/1440, \"[h]:mm:ss\"), \"N/A\")";

            worksheet.Cells[8, 15].Value = "Longs Average Profit";
            worksheet.Cells[8, 16].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"Long\", I:I), 3), \"N/A\")";

            worksheet.Cells[9, 15].Value = "Shorts Average Profit";
            worksheet.Cells[9, 16].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"Short\", I:I), 3), \"N/A\")";

            worksheet.Cells[10, 15].Value = "MAC-D Average Profit";
            worksheet.Cells[10, 16].Formula = "IFERROR(ROUND(AVERAGEIF(F:F, \"MAC-D\", I:I), 3), \"N/A\")";

            worksheet.Cells[11, 15].Value = "SMA Average Profit";
            worksheet.Cells[11, 16].Formula = "IFERROR(ROUND(AVERAGEIF(F:F, \"SMAExpansion\", I:I), 3), \"N/A\")";

            worksheet.Cells[12, 15].Value = "SMA Short Combined Average";
            worksheet.Cells[12, 16].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, E:E, \"Short\", F:F, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[13, 15].Value = "SMA Long Combined Average";
            worksheet.Cells[13, 16].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, E:E, \"Long\", F:F, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[14, 15].Value = "MAC-D Long Combined Average";
            worksheet.Cells[14, 16].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, E:E, \"Long\", F:F, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[15, 15].Value = "MAC-D Short Combined Average";
            worksheet.Cells[15, 16].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, E:E, \"Short\", F:F, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[16, 15].Value = "Win Rate";
            worksheet.Cells[16, 16].Formula = "IFERROR(ROUND(COUNTIF(I:I, \">0\") / COUNT(I:I), 3), \"N/A\")";

            worksheet.Cells[17, 15].Value = "Sharpe Ratio";
            worksheet.Cells[17, 16].Formula = "IFERROR(ROUND(AVERAGE(I:I) / STDEV(I:I), 3), \"N/A\")";

            worksheet.Cells[18, 15].Value = "Profit Factor";
            worksheet.Cells[18, 16].Formula = "IFERROR(ROUND(SUMIF(I:I, \">0\") / ABS(SUMIF(I:I, \"<0\")), 3), \"N/A\")";

            worksheet.Cells[20, 15].Value = "Total Long Trades";
            worksheet.Cells[20, 16].Formula = "COUNTIF(E:E, \"Long\")";

            worksheet.Cells[21, 15].Value = "Total Short Trades";
            worksheet.Cells[21, 16].Formula = "COUNTIF(E:E, \"Short\")";

            worksheet.Cells[22, 15].Value = "Maximum Profit per Trade";
            worksheet.Cells[22, 16].Formula = "IFERROR(ROUND(MAX(I:I), 3), \"N/A\")";

            worksheet.Cells[23, 15].Value = "Maximum Loss per Trade";
            worksheet.Cells[23, 16].Formula = "IFERROR(ROUND(MIN(I:I), 3), \"N/A\")";

            // New statistics for US market hours
            worksheet.Cells[25, 15].Value = "US Market Hours Trades";
            worksheet.Cells[25, 16].Formula = "COUNTIF(K:K, \"Yes\")";

            worksheet.Cells[26, 15].Value = "US Market Hours Profit";
            worksheet.Cells[26, 16].Formula = "IFERROR(ROUND(SUMIFS(I:I, K:K, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[27, 15].Value = "US Market Hours Average Profit";
            worksheet.Cells[27, 16].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, K:K, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[28, 15].Value = "US Market Hours Win Rate";
            worksheet.Cells[28, 16].Formula = "IFERROR(ROUND(COUNTIFS(I:I, \">0\", K:K, \"Yes\") / COUNTIFS(K:K, \"Yes\"), 3), \"N/A\")";

            // Adding funds added for different leverage levels
            for (int leverageLevel = 5; leverageLevel <= 25; leverageLevel += 5)
            {
                int leverageRow = 30 + leverageLevel / 5;
                worksheet.Cells[leverageRow, 15].Value = $"Funds added {leverageLevel}x";
                worksheet.Cells[leverageRow, 16].Formula = $"SUMIFS(J:J, C:C, {leverageLevel})";
            }

            // Adding funds added for different intervals
            string[] intervals = { "1m", "5m", "15m", "30m", "1h" };
            for (int i = 0; i < intervals.Length; i++)
            {
                int intervalRow = 30 + i;
                worksheet.Cells[intervalRow, 17].Value = $"Funds added {intervals[i]}";
                worksheet.Cells[intervalRow, 18].Formula = $"SUMIFS(J:J, D:D, \"{intervals[i]}\")";
            }

            package.Save();
        }
    }
}
