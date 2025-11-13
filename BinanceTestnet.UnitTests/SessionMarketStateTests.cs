using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using RestSharp;
using BinanceTestnet.Database;
using BinanceTestnet.MarketAnalysis;
using BinanceTestnet.Trading;
using Xunit;

namespace BinanceTestnet.UnitTests
{
    public class SessionMarketStateTests
    {
        [Fact]
        public async Task GenerateHtmlReport_Includes_SessionMarketState()
        {
            // Arrange
            var tempDb = Path.Combine(Path.GetTempPath(), $"tests_{Guid.NewGuid():N}.db");
            try
            {
                // Create a minimal Trades table used by TradeLogger.GetTrades
                using (var conn = new SqliteConnection($"Data Source={tempDb};"))
                {
                    conn.Open();
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        CREATE TABLE Trades (
                            TradeId INTEGER PRIMARY KEY AUTOINCREMENT,
                            SessionId TEXT,
                            Symbol TEXT,
                            TradeType TEXT,
                            Signal TEXT,
                            EntryTime DATETIME,
                            EntryPrice REAL,
                            TakeProfit REAL,
                            StopLoss REAL,
                            Leverage REAL,
                            Interval TEXT,
                            KlineTimestamp DATETIME,
                            TakeProfitMultiplier REAL,
                            MarginPerTrade REAL,
                            LiquidationPrice REAL,
                            MaintenanceMarginRate REAL,
                            ExitTime DATETIME,
                            ExitPrice REAL,
                            Profit REAL,
                            Duration INTEGER,
                            FundsAdded REAL,
                            IsNearLiquidation INTEGER
                        );";
                    cmd.ExecuteNonQuery();

                    // Insert sample trades for session 's1'
                    cmd.CommandText = @"
                        INSERT INTO Trades (SessionId, Symbol, TradeType, Signal, EntryTime, EntryPrice, TakeProfit, StopLoss, Leverage, Interval, KlineTimestamp, TakeProfitMultiplier, MarginPerTrade)
                        VALUES (@sid, @sym, @tt, @sig, @et, @ep, @tp, @sl, @lev, @intv, @kt, @tpm, @mp);";
                    cmd.Parameters.AddWithValue("@sid", "s1");
                    cmd.Parameters.AddWithValue("@sym", "BTCUSDT");
                    cmd.Parameters.AddWithValue("@tt", "Long");
                    cmd.Parameters.AddWithValue("@sig", "T1");
                    cmd.Parameters.AddWithValue("@et", DateTime.UtcNow.AddHours(-2));
                    cmd.Parameters.AddWithValue("@ep", 100.0m);
                    cmd.Parameters.AddWithValue("@tp", 110.0m);
                    cmd.Parameters.AddWithValue("@sl", 95.0m);
                    cmd.Parameters.AddWithValue("@lev", 5.0m);
                    cmd.Parameters.AddWithValue("@intv", "5m");
                    cmd.Parameters.AddWithValue("@kt", DateTime.UtcNow.AddHours(-2));
                    cmd.Parameters.AddWithValue("@tpm", 2.0m);
                    cmd.Parameters.AddWithValue("@mp", 20.0m);
                    cmd.ExecuteNonQuery();

                    // second trade later in session
                    cmd.Parameters.Clear();
                    cmd.CommandText = @"
                        INSERT INTO Trades (SessionId, Symbol, TradeType, Signal, EntryTime, EntryPrice, TakeProfit, StopLoss, Leverage, Interval, KlineTimestamp, TakeProfitMultiplier, MarginPerTrade, ExitTime, ExitPrice, Profit)
                        VALUES (@sid2, @sym2, @tt2, @sig2, @et2, @ep2, @tp2, @sl2, @lev2, @intv2, @kt2, @tpm2, @mp2, @exitT, @exitP, @profit);";
                    cmd.Parameters.AddWithValue("@sid2", "s1");
                    cmd.Parameters.AddWithValue("@sym2", "BTCUSDT");
                    cmd.Parameters.AddWithValue("@tt2", "Long");
                    cmd.Parameters.AddWithValue("@sig2", "T2");
                    cmd.Parameters.AddWithValue("@et2", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@ep2", 102.0m);
                    cmd.Parameters.AddWithValue("@tp2", 112.0m);
                    cmd.Parameters.AddWithValue("@sl2", 97.0m);
                    cmd.Parameters.AddWithValue("@lev2", 5.0m);
                    cmd.Parameters.AddWithValue("@intv2", "5m");
                    cmd.Parameters.AddWithValue("@kt2", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@tpm2", 2.0m);
                    cmd.Parameters.AddWithValue("@mp2", 20.0m);
                    cmd.Parameters.AddWithValue("@exitT", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@exitP", 103.0m);
                    cmd.Parameters.AddWithValue("@profit", 1.5m);
                    cmd.ExecuteNonQuery();
                }

                var tradeLogger = new TradeLogger(tempDb);
                var marketAnalyzer = new MarketContextAnalyzer(new RestClient(), LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<MarketContextAnalyzer>());
                var generator = new HtmlReportGenerator(tradeLogger, marketAnalyzer);
                var settings = new ReportSettings { SessionReferenceMode = "fixed", SessionReferenceSymbol = "BTCUSDT", Interval = "5m" };

                // Act
                var html = await generator.GenerateHtmlReport("s1", settings);

                // Assert
                Assert.Contains("Session Market State", html);
            }
            finally
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
