using OfficeOpenXml;
using System;
using System.IO;

public class ExcelWriter
{
    private readonly string _folderPath;
    private readonly string _filePath;

    public ExcelWriter(string folderPath = "Excels", string fileName = "ClosedTradesNoSMAShort.xlsx")
    {
        _folderPath = folderPath;
        _filePath = Path.Combine(_folderPath, fileName);

        if (!Directory.Exists(_folderPath))
        {
            Directory.CreateDirectory(_folderPath);
        }
    }

    public void WriteClosedTradeToExcel(Trade trade)
    {
        bool fileExists = File.Exists(_filePath);

        using (var package = new ExcelPackage(new FileInfo(_filePath)))
        {
            ExcelWorksheet worksheet = fileExists ? package.Workbook.Worksheets[0] : package.Workbook.Worksheets.Add("Closed Trades");

            // Ensure headers are written only if the sheet is newly created
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
                worksheet.Cells[1, 10].Value = "US Market Hours"; // New column for market hours
            }

            // Write closed trade details
            int row = worksheet.Dimension?.Rows + 1 ?? 2;

            decimal fundsAdded = trade.Profit.HasValue ? trade.Profit.Value * trade.InitialMargin : 0;

            worksheet.Cells[row, 1].Value = trade.Id;
            worksheet.Cells[row, 2].Value = trade.Symbol;
            worksheet.Cells[row, 3].Value = trade.Leverage;
            worksheet.Cells[row, 4].Value = trade.IsLong ? "Long" : "Short";
            worksheet.Cells[row, 5].Value = trade.Signal;
            worksheet.Cells[row, 6].Value = trade.EntryTimestamp.ToString("MM/dd HH:mm");
            worksheet.Cells[row, 7].Value = trade.Duration.TotalMinutes;
            worksheet.Cells[row, 8].Value = trade.Profit.HasValue ? trade.Profit : 0;
            worksheet.Cells[row, 9].Value = fundsAdded;

            // Determine if trade was during US market hours
            bool isUSMarketHours = trade.EntryTimestamp.TimeOfDay >= new TimeSpan(14, 30, 0) && trade.EntryTimestamp.TimeOfDay <= new TimeSpan(21, 0, 0); // 9:30 AM to 4:00 PM EST is 14:30 to 21:00 UTC
            worksheet.Cells[row, 10].Value = isUSMarketHours ? "Yes" : "No"; // New column value

            // Update dashboard statistics
            worksheet.Cells[5, 12].Value = "Total Funds Added";
            worksheet.Cells[5, 13].Formula = "ROUND(SUM(I:I), 3)";

            worksheet.Cells[6, 12].Value = "Total Closed Trades";
            worksheet.Cells[6, 13].Formula = "COUNTA(A:A) - 1";

            worksheet.Cells[7, 12].Value = "Average Duration hh:mm:ss";
            worksheet.Cells[7, 13].Formula = "IFERROR(TEXT(AVERAGE(G:G)/1440, \"[h]:mm:ss\"), \"N/A\")";


            worksheet.Cells[8, 12].Value = "Longs Average Profit";
            worksheet.Cells[8, 13].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Long\", I:I), 3), \"N/A\")";

            worksheet.Cells[9, 12].Value = "Shorts Average Profit";
            worksheet.Cells[9, 13].Formula = "IFERROR(ROUND(AVERAGEIF(D:D, \"Short\", I:I), 3), \"N/A\")";

            worksheet.Cells[10, 12].Value = "MAC-D Average Profit";
            worksheet.Cells[10, 13].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"MAC-D\", I:I), 3), \"N/A\")";

            worksheet.Cells[11, 12].Value = "SMA Average Profit";
            worksheet.Cells[11, 13].Formula = "IFERROR(ROUND(AVERAGEIF(E:E, \"SMAExpansion\", I:I), 3), \"N/A\")";

            worksheet.Cells[12, 12].Value = "SMA Short Combined Average";
            worksheet.Cells[12, 13].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, D:D, \"Short\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[13, 12].Value = "SMA Long Combined Average";
            worksheet.Cells[13, 13].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, D:D, \"Long\", E:E, \"SMAExpansion\"), 3), \"N/A\")";

            worksheet.Cells[14, 12].Value = "MAC-D Long Combined Average";
            worksheet.Cells[14, 13].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, D:D, \"Long\", E:E, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[15, 12].Value = "MAC-D Short Combined Average";
            worksheet.Cells[15, 13].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, D:D, \"Short\", E:E, \"MAC-D\"), 3), \"N/A\")";

            worksheet.Cells[16, 12].Value = "Win Rate";
            worksheet.Cells[16, 13].Formula = "IFERROR(ROUND(COUNTIF(I:I, \">0\") / COUNT(I:I), 3), \"N/A\")";

            worksheet.Cells[17, 12].Value = "Sharpe Ratio";
            worksheet.Cells[17, 13].Formula = "IFERROR(ROUND(AVERAGE(I:I) / STDEV(I:I), 3), \"N/A\")";

            worksheet.Cells[18, 12].Value = "Profit Factor";
            worksheet.Cells[18, 13].Formula = "IFERROR(ROUND(SUMIF(I:I, \">0\") / ABS(SUMIF(I:I, \"<0\")), 3), \"N/A\")";

            worksheet.Cells[20, 12].Value = "Total Long Trades";
            worksheet.Cells[20, 13].Formula = "COUNTIF(D:D, \"Long\")";

            worksheet.Cells[21, 12].Value = "Total Short Trades";
            worksheet.Cells[21, 13].Formula = "COUNTIF(D:D, \"Short\")";

            worksheet.Cells[22, 12].Value = "Maximum Profit per Trade";
            worksheet.Cells[22, 13].Formula = "IFERROR(ROUND(MAX(I:I), 3), \"N/A\")";

            worksheet.Cells[23, 12].Value = "Maximum Loss per Trade";
            worksheet.Cells[23, 13].Formula = "IFERROR(ROUND(MIN(I:I), 3), \"N/A\")";

            // New statistics for US market hours
            worksheet.Cells[25, 12].Value = "US Market Hours Trades";
            worksheet.Cells[25, 13].Formula = "COUNTIF(J:J, \"Yes\")";

            worksheet.Cells[26, 12].Value = "US Market Hours Profit";
            worksheet.Cells[26, 13].Formula = "IFERROR(ROUND(SUMIFS(I:I, J:J, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[27, 12].Value = "US Market Hours Average Profit";
            worksheet.Cells[27, 13].Formula = "IFERROR(ROUND(AVERAGEIFS(I:I, J:J, \"Yes\"), 3), \"N/A\")";

            worksheet.Cells[28, 12].Value = "US Market Hours Win Rate";
            worksheet.Cells[28, 13].Formula = "IFERROR(ROUND(COUNTIFS(I:I, \">0\", J:J, \"Yes\") / COUNTIFS(J:J, \"Yes\"), 3), \"N/A\")";

            package.Save();
        }
    }
}
