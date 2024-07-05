using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OfficeOpenXml;

public class TradeRecorder
{
    private List<TradeRecord> tradeRecords;

    public TradeRecorder(List<TradeRecord> tradeRecords)
    {
        this.tradeRecords = tradeRecords;
    }

public void WriteTradesToCsv(string filePath)
{
    var csvBuilder = new StringBuilder();

    // Check if the file already exists, if not, write the header
    if (!File.Exists(filePath))
    {
        csvBuilder.AppendLine("Symbol,EntryPrice,ExitPrice,IsLong,Quantity,Signal,Profit,Timestamp");
    }

    foreach (var trade in tradeRecords)
    {
        csvBuilder.AppendLine($"{trade.Symbol},{trade.EntryPrice},{trade.ExitPrice},{trade.IsLong},{trade.Quantity},{trade.Signal},{trade.Profit},{trade.Timestamp:O}");
    }

    File.AppendAllText(filePath, csvBuilder.ToString());
}

    public void WriteTradesToExcel(string filePath)
    {
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Required for EPPlus

        // Load existing Excel file if it exists
        FileInfo file = new FileInfo(filePath);
        ExcelPackage package;

        if (file.Exists)
        {
            using (package = new ExcelPackage(file))
            {
                var worksheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "Trades") ?? package.Workbook.Worksheets.Add("Trades");

                int lastRow = worksheet.Dimension?.Rows ?? 1;
                for (int i = 0; i < tradeRecords.Count; i++)
                {
                    var trade = tradeRecords[i];
                    worksheet.Cells[lastRow + i + 1, 1].Value = trade.Symbol;
                    worksheet.Cells[lastRow + i + 1, 2].Value = trade.EntryPrice;
                    worksheet.Cells[lastRow + i + 1, 3].Value = trade.ExitPrice;
                    worksheet.Cells[lastRow + i + 1, 4].Value = trade.IsLong;
                    worksheet.Cells[lastRow + i + 1, 5].Value = trade.Quantity;
                    worksheet.Cells[lastRow + i + 1, 6].Value = trade.Signal;
                    worksheet.Cells[lastRow + i + 1, 7].Value = trade.Profit;
                    worksheet.Cells[lastRow + i + 1, 8].Value = trade.Timestamp;
                }

                package.Save();
            }
        }
        else
        {
            // File doesn't exist, create a new one
            using (package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Trades");

                // Add headers
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
                    var trade = tradeRecords[i];
                    worksheet.Cells[i + 2, 1].Value = trade.Symbol;
                    worksheet.Cells[i + 2, 2].Value = trade.EntryPrice;
                    worksheet.Cells[i + 2, 3].Value = trade.ExitPrice;
                    worksheet.Cells[i + 2, 4].Value = trade.IsLong;
                    worksheet.Cells[i + 2, 5].Value = trade.Quantity;
                    worksheet.Cells[i + 2, 6].Value = trade.Signal;
                    worksheet.Cells[i + 2, 7].Value = trade.Profit;
                    worksheet.Cells[i + 2, 8].Value = trade.Timestamp;
                }

                package.SaveAs(file);
            }
        }
    }

}
