using System;
using System.IO;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using BinanceTestnet.Database;
using BinanceTestnet.Enums;
using BinanceTestnet.Models;
using BinanceTestnet.Strategies;
using BinanceTestnet.Trading;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using BinanceLive.Services; // Added for DataFetchingUtility

namespace BinanceTestnet.Tools
{
    // Phase 1 scaffold: JSON-only config to avoid adding new deps. YAML can be supported later.
    public class MultiBacktestConfig
    {
        public string Strategy { get; set; } = "MACDStandard"; // global default
        public List<string> Timeframes { get; set; } = new();
        public Dictionary<string, List<string>> SymbolSets { get; set; } = new();
        public List<ExitModeConfig> ExitModes { get; set; } = new();
        public OutputConfig Output { get; set; } = new();
        public HistoricalConfig? Historical { get; set; }
    }

    public class ExitModeConfig
    {
    public string Name { get; set; } = "fixed";
    public List<RiskProfileConfig>? RiskProfiles { get; set; }
    }

    public class RiskProfileConfig
    {
        public string Name { get; set; } = string.Empty;
        public decimal TpMultiplier { get; set; }
        public decimal SlMultiplier { get; set; }
    }

    public class OutputConfig
    {
        public string Directory { get; set; } = "results/multi";
        public string Format { get; set; } = "csv";
    }

    public class HistoricalConfig
    {
        // ISO 8601 UTC string, e.g., 2025-10-01T00:00:00Z
        public string? StartUtc { get; set; }
        // Per-request size (Binance futures max ~1500)
        public int BatchSize { get; set; } = 1500;
        // Optional hard cap to avoid excessive memory
        public int? MaxCandles { get; set; }
    }

    public class MultiBacktestRunner
    {
        private readonly RestClient _client;
        private readonly string _apiKey;
        private readonly string _databasePath;
        private readonly ILogger<OrderManager> _logger;
        private readonly IExchangeInfoProvider _exchangeInfoProvider;

        public MultiBacktestRunner(RestClient client, string apiKey, string databasePath,
            ILogger<OrderManager> logger, IExchangeInfoProvider exchangeInfoProvider)
        {
            _client = client;
            _apiKey = apiKey;
            _databasePath = databasePath;
            _logger = logger;
            _exchangeInfoProvider = exchangeInfoProvider;
        }

        public static MultiBacktestConfig LoadConfig(string path)
        {
            // Phase 1: JSON-only; later: detect .yml and use YamlDotNet
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<MultiBacktestConfig>(json)
                   ?? throw new InvalidOperationException("Invalid config");
        }

        // (Deprecated duplicate) Original RunAsync removed; see updated implementation below honoring output directory.

        private async Task RunOneCombination(
            string strategyName,
            string timeframe,
            string setName,
            List<string> symbols,
            ExitModeConfig exitMode,
            RiskProfileConfig? riskProfile,
            CancellationToken ct)
        {
            // Unique session per combo
            var sessionId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString()[..6]}";

            // Wallet and order manager per run
            var wallet = new Wallet(initialBalance: 1000m);

            // Interpret exit config to order parameters
            // For now, keep leverage/margin/stop defaults from environment; adjust TP/SL via OrderManager ctor inputs.
            decimal leverage = 10m;
            decimal marginPerTrade = 10m;
            decimal takeProfit = riskProfile?.TpMultiplier ?? 1.0m;
            decimal stopLoss = riskProfile?.SlMultiplier ?? 1.0m;
            decimal tpIteration = 0m;
            var opMode = OperationMode.Backtest;
            var tradeDirection = SelectedTradeDirection.Both;
            var selectedStrategy = MapStrategy(strategyName);

            var orderManager = new OrderManager(
                wallet: wallet,
                leverage: leverage,
                operationMode: opMode,
                interval: timeframe,
                takeProfit: takeProfit,
                stopLoss: stopLoss,
                tradeDirection: tradeDirection,
                tradingStrategy: selectedStrategy,
                client: _client,
                tpIteration: tpIteration,
                margin: marginPerTrade,
                databasePath: _databasePath,
                sessionId: sessionId,
                exchangeInfoProvider: _exchangeInfoProvider,
                logger: _logger,
                onTradeEntered: (_, _, _, _, _) => { });

            // No trailing config for fixed mode
            orderManager.UpdateTrailingConfig(false, null, null);

            // Mute Wallet console output during bulk backtests to avoid UI freeze
            Wallet.EnableConsoleOutput = true;

            // Diagnostic: print which strategy was selected for this run
            Console.WriteLine($"Running strategy: {selectedStrategy}");

            // Strategy runner (we’ll run per symbol by feeding symbol-specific klines)
            var selectedStrategies = new List<SelectedTradingStrategy> { selectedStrategy };
            var runner = new StrategyRunner(_client, _apiKey, symbols, timeframe, wallet, orderManager, selectedStrategies);

            string endUtc = null;
            // Track per-symbol fetched counts to compute a conservative candlesTested value
            var perSymbolCounts = new List<int>();
            foreach (var symbol in symbols)
            {
                if (ct.IsCancellationRequested) return;
                // Fetch historical klines for the symbol/timeframe
                var history = await FetchHistoryAsync(symbol, timeframe, ct);
                if (history == null || history.Count == 0)
                {
                    Console.WriteLine($"[WARN] No history fetched for {symbol} {timeframe}; skipping.");
                    continue;
                }
                perSymbolCounts.Add(history.Count);
                await runner.RunStrategiesOnHistoricalDataAsync(history);
                try
                {
                    // Use the last kline close time as the run end time (UTC)
                    var lastCloseMs = history.Last().CloseTime;
                    var dto = DateTimeOffset.FromUnixTimeMilliseconds(lastCloseMs).UtcDateTime;
                    endUtc = dto.ToString("yyyy-MM-ddTHH:mm:ss'Z'");
                }
                catch { /* ignore */ }
            }

            // Compute a conservative candlesTested as the minimum number of candles fetched across symbols (if any)
            int? candlesTested = null;
            if (perSymbolCounts.Count > 0)
            {
                candlesTested = perSymbolCounts.Min();
            }

            // Compute metrics and append CSV (include endUtc and candlesTested when available)
            await WriteCsvRowAsync(sessionId, timeframe, setName, strategyName, _historical?.StartUtc, endUtc, candlesTested, exitMode, riskProfile, symbols, outputDirectory: _pendingOutputDir);
        }

        // Capture desired output directory from config at run start
    private string _pendingOutputDir = string.Empty;
    private HistoricalConfig? _historical;

        public async Task RunAsync(MultiBacktestConfig cfg, CancellationToken ct = default, string? baseDirectory = null)
        {
            // Store output directory (honor config) before looping
            var desired = string.IsNullOrWhiteSpace(cfg.Output.Directory) ? "results/multi" : cfg.Output.Directory;
            // If relative, resolve relative to the config file directory when provided
            _pendingOutputDir = Path.IsPathRooted(desired)
                ? desired
                : Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), desired);
            _historical = cfg.Historical;
            Directory.CreateDirectory(_pendingOutputDir);
            // Overwrite results file at start of batch run
            var csvPath = Path.Combine(_pendingOutputDir, "multi_results.csv");
            if (System.IO.File.Exists(csvPath)) System.IO.File.Delete(csvPath);

            foreach (var timeframe in cfg.Timeframes)
            {
                foreach (var kvp in cfg.SymbolSets)
                {
                    var setName = kvp.Key;
                    var symbols = kvp.Value;

                    foreach (var exit in cfg.ExitModes)
                    {
                        if (ct.IsCancellationRequested) return;
                        if (exit.RiskProfiles?.Any() == true)
                        {
                            foreach (var rp in exit.RiskProfiles!)
                            {
                                if (ct.IsCancellationRequested) return;
                                await RunOneCombination(cfg.Strategy, timeframe, setName, symbols, exit, rp, ct);
                            }
                        }
                    }
                }
            }
        }

        private async Task<List<Kline>> FetchHistoryAsync(string symbol, string interval, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(_historical?.StartUtc) && DateTime.TryParse(
                    _historical.StartUtc,
                    provider: null,
                    styles: System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    result: out var startUtc))
            {
                int batch = Math.Clamp(_historical!.BatchSize, 1, 1500);
                int? max = _historical.MaxCandles;
                return await FetchRangeFuturesAsync(symbol, interval, startUtc, batch, max, ct);
            }
            // Fallback: single call ~200 candles
            return await DataFetchingUtility.FetchHistoricalData(_client, symbol, interval);
        }

        private async Task<List<Kline>> FetchRangeFuturesAsync(string symbol, string interval, DateTime startUtc, int batchSize, int? maxCandles, CancellationToken ct)
        {
            // Single-page fetch: request up to batchSize klines starting at startUtc and return them.
            var all = new List<Kline>(Math.Max(batchSize, 200));
            long startMs = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

            var req = new RestRequest("/fapi/v1/klines", Method.Get);
            req.AddParameter("symbol", symbol);
            req.AddParameter("interval", interval);
            req.AddParameter("startTime", startMs);
            req.AddParameter("limit", batchSize);

            var resp = await _client.ExecuteAsync<List<List<object>>>(req, ct);
            if (!resp.IsSuccessful || string.IsNullOrEmpty(resp.Content)) return all;

            var data = JsonConvert.DeserializeObject<List<List<object>>>(resp.Content);
            if (data == null || data.Count == 0) return all;

            int collected = 0;
            foreach (var k in data)
            {
                var kl = new Kline
                {
                    Symbol = symbol,
                    OpenTime = (long)k[0],
                    Open = decimal.Parse(k[1]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                    High = decimal.Parse(k[2]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                    Low = decimal.Parse(k[3]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                    Close = decimal.Parse(k[4]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(k[5]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture),
                    CloseTime = (long)k[6],
                    NumberOfTrades = int.Parse(k[8]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture)
                };
                all.Add(kl);
                collected++;
                if (maxCandles.HasValue && collected >= maxCandles.Value)
                    break;
            }
            return all;
        }

        private async Task WriteCsvRowAsync(
            string sessionId,
            string timeframe,
            string setName,
            string strategyName,
            string? startUtc,
            string? endUtc,
            int? candlesTested,
            ExitModeConfig exitMode,
            RiskProfileConfig? riskProfile,
            List<string> symbols,
            string outputDirectory)
        {
            var tradeLogger = new TradeLogger(_databasePath);
            var trades = tradeLogger.GetTrades(sessionId);

            // Aggregate metrics
            int total = trades.Count;
            int wins = trades.Count(t => (t.Profit ?? 0m) > 0m);
            int losses = total - wins;
            decimal net = trades.Sum(t => t.Profit ?? 0m);
            var winProfits = trades.Where(t => (t.Profit ?? 0m) > 0m).Select(t => t.Profit!.Value).ToList();
            var lossProfits = trades.Where(t => (t.Profit ?? 0m) <= 0m).Select(t => Math.Abs(t.Profit ?? 0m)).ToList();
            decimal avgWin = winProfits.Any() ? winProfits.Average() : 0m;
            decimal avgLoss = lossProfits.Any() ? lossProfits.Average() : 0m;
            decimal payoff = (avgLoss > 0m) ? avgWin / avgLoss : 0m;
            decimal winRate = total > 0 ? (decimal)wins / total * 100m : 0m;
            decimal expectancy = (avgWin * (winRate / 100m)) - (avgLoss * (1m - (winRate / 100m)));
            int maxConsecLoss = CalcMaxConsecutiveLosses(trades);
            double avgDuration = trades.Any() ? trades.Average(t => (double)t.Duration) : 0.0;

            var topSymbols = trades
                .GroupBy(t => t.Symbol)
                .Select(g => new { Sym = g.Key, P = g.Sum(x => x.Profit ?? 0m) })
                .OrderByDescending(x => x.P)
                .Take(3).Select(x => x.Sym);
            var bottomSymbols = trades
                .GroupBy(t => t.Symbol)
                .Select(g => new { Sym = g.Key, P = g.Sum(x => x.Profit ?? 0m) })
                .OrderBy(x => x.P)
                .Take(3).Select(x => x.Sym);

            var csvPath = Path.Combine(outputDirectory, "multi_results.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

            bool writeHeader = !File.Exists(csvPath);
            using (var sw = new StreamWriter(csvPath, append: true))
            {
                if (writeHeader)
                {
                    await sw.WriteLineAsync("sessionId,timeframe,symbolSet,strategy,startUtc,endUtc,candlesTested,exitMode,tpMult,slMult,trades,winRate,netPnl,avgWin,avgLoss,payoff,expectancy,maxConsecLoss,avgDuration,topSymbol,bottomSymbol");
                }
                string row = string.Join(",", new[]
                {
                    sessionId,
                    timeframe,
                    setName,
                    strategyName ?? "",
                    startUtc ?? "",
                    endUtc ?? "",
                    (candlesTested.HasValue ? candlesTested.Value.ToString() : ""),
                    exitMode.Name,
                    (riskProfile?.TpMultiplier ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (riskProfile?.SlMultiplier ?? 0m).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    total.ToString(),
                    winRate.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    net.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    avgWin.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    avgLoss.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    payoff.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    expectancy.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    maxConsecLoss.ToString(),
                    avgDuration.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                    topSymbols.FirstOrDefault() ?? "",
                    bottomSymbols.FirstOrDefault() ?? ""
                });
                await sw.WriteLineAsync(row);
            }
        }

        private static int CalcMaxConsecutiveLosses(List<Trade> trades)
        {
            int maxStreak = 0, cur = 0;
            foreach (var t in trades.OrderBy(t => t.EntryTime))
            {
                if ((t.Profit ?? 0m) <= 0m) { cur++; maxStreak = Math.Max(maxStreak, cur); }
                else { cur = 0; }
            }
            return maxStreak;
        }

        private static SelectedTradingStrategy MapStrategy(string strategyName)
        {
            if (Enum.TryParse<SelectedTradingStrategy>(strategyName, ignoreCase: true, out var val))
                return val;
            throw new ArgumentException($"Unknown strategy: {strategyName}");
        }
    }
}
