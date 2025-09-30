using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Globalization;
using RestSharp;

namespace BinanceTestnet.MarketAnalysis
{
    public interface IMarketContextAnalyzer
    {
        Task<MarketContext> AnalyzePeriodAsync(string symbol, DateTime startTime, DateTime endTime, string tradingTimeframe, List<Trade> allTrades);
        Task<List<MarketRegimeSegment>> GetRegimeSegmentsAsync(DateTime startTime, DateTime endTime, string tradingTimeframe, string symbol = "BTCUSDT");
        MarketRegime AnalyzeCurrentRegime(BTCTrendAnalysis trendAnalysis, List<Kline> klines = null);
        
    }

    public class MarketContext
    {
        // Trading context
        public string TradingTimeframe { get; set; }
        public string AnalysisTimeframe { get; set; }
        
        // Original spec - general market (1h/4h)
        public MarketRegime GeneralMarketRegime { get; set; }
        public BTCTrendAnalysis GeneralTrendAnalysis { get; set; }
        
        // New - trading-aligned (dynamic timeframes)
        public MarketRegime TradingAlignedRegime { get; set; }
        public BTCTrendAnalysis TradingTrendAnalysis { get; set; }
        
        // Performance by both regimes
        public Dictionary<MarketRegimeType, StrategyPerformance> PerformanceByGeneralRegime { get; set; } = new();
        public Dictionary<MarketRegimeType, StrategyPerformance> PerformanceByTradingRegime { get; set; } = new();
        
        // Regime segments for timeline analysis
        public List<MarketRegimeSegment> RegimeSegments { get; set; } = new();
    }

    public class StrategyPerformance
    {
        public int TradeCount { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal WinRate { get; set; }
        public decimal AvgProfit { get; set; }
        public decimal MaxDrawdown { get; set; }
    }

    public class MarketContextAnalyzer : IMarketContextAnalyzer
    {
        private readonly RestClient _client;
        private readonly ILogger _logger; // Change to non-generic

        private List<Trade> _allTrades;

        public MarketContextAnalyzer(RestClient client, ILogger logger) // Change parameter type
        {
            _client = client;
            _logger = logger;
        }

        public async Task<MarketContext> AnalyzePeriodAsync(string symbol, DateTime startTime, DateTime endTime, string tradingTimeframe, List<Trade> allTrades)
        {
            var context = new MarketContext
            {
                TradingTimeframe = tradingTimeframe
            };

            try
            {
                _logger.LogInformation("Analyzing dual market context for {Symbol} ({TradingTF}) from {Start} to {End}",
                    symbol, tradingTimeframe, startTime, endTime);

                // 1. General Market Analysis (1h/4h as per original spec)
                context.GeneralTrendAnalysis = await GetBTCTrendAnalysisAsync(symbol, startTime, endTime, "1h", "4h");
                context.GeneralMarketRegime = AnalyzeCurrentRegime(context.GeneralTrendAnalysis);

                // 2. Trading-Aligned Analysis (dynamic based on trading timeframe)
                var (tradingPrimary, tradingSecondary) = GetTradingAlignedTimeframes(tradingTimeframe);
                context.TradingTrendAnalysis = await GetBTCTrendAnalysisAsync(symbol, startTime, endTime, tradingPrimary, tradingSecondary);
                context.TradingAlignedRegime = AnalyzeCurrentRegime(context.TradingTrendAnalysis);

                // 3. Calculate performance for both regimes (only if trades are provided)
                if (allTrades != null && allTrades.Any())
                {
                    context.PerformanceByGeneralRegime = CalculatePerformanceByRegime(allTrades, context.GeneralMarketRegime);
                    context.PerformanceByTradingRegime = CalculatePerformanceByRegime(allTrades, context.TradingAlignedRegime);
                }
                else
                {
                    context.PerformanceByGeneralRegime = new Dictionary<MarketRegimeType, StrategyPerformance>();
                    context.PerformanceByTradingRegime = new Dictionary<MarketRegimeType, StrategyPerformance>();
                }

                _logger.LogInformation("Dual market context analysis completed: General={GeneralRegime}, Trading={TradingRegime}",
                    context.GeneralMarketRegime.Type, context.TradingAlignedRegime.Type);

                return context;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyze dual market context for {Symbol}", symbol);

                // Return minimal context instead of throwing
                context.GeneralMarketRegime = new MarketRegime { Type = MarketRegimeType.Unknown };
                context.TradingAlignedRegime = new MarketRegime { Type = MarketRegimeType.Unknown };
                return context;
            }
        }

        private (string primaryTF, string secondaryTF) GetTradingAlignedTimeframes(string tradingTimeframe)
        {
            return tradingTimeframe.ToLower() switch
            {
                "1m" => ("5m", "15m"),
                "5m" => ("15m", "1h"),
                "15m" => ("1h", "4h"),
                "30m" => ("1h", "4h"),
                "1h" => ("4h", "1d"),
                "4h" => ("1d", "3d"),
                "1d" => ("3d", "1w"),
                _ => ("1h", "4h")
            };
        }

        private Dictionary<MarketRegimeType, StrategyPerformance> CalculatePerformanceByRegime(List<Trade> trades, MarketRegime regime)
        {
            var performance = new Dictionary<MarketRegimeType, StrategyPerformance>();

            // For now, just calculate overall performance
            // In future, we can segment by actual regime periods
            var regimeTrades = trades.Where(t =>
                t.EntryTime >= regime.PeriodStart &&
                t.EntryTime <= regime.PeriodEnd).ToList();

            performance[regime.Type] = new StrategyPerformance
            {
                TradeCount = regimeTrades.Count,
                TotalPnL = regimeTrades.Sum(t => t.Profit ?? 0),
                WinRate = regimeTrades.Count > 0 ? (decimal)regimeTrades.Count(t => t.Profit > 0) / regimeTrades.Count * 100 : 0,
                AvgProfit = regimeTrades.Count > 0 ? regimeTrades.Average(t => t.Profit ?? 0) : 0
            };

            return performance;
        }
        
        public MarketRegime AnalyzeCurrentRegime(BTCTrendAnalysis trendAnalysis, List<Kline> klines = null)
        {
            // Your new implementation with klines parameter
            if (trendAnalysis?.Primary1H == null)
            {
                _logger.LogWarning("No primary trend analysis available for regime detection");
                return new MarketRegime { Type = MarketRegimeType.Unknown };
            }

            var primary = trendAnalysis.Primary1H;
            var regime = new MarketRegime
            {
                AnalysisTime = DateTime.UtcNow,
                PeriodStart = trendAnalysis.Primary1H.Timestamp.AddHours(-24),
                PeriodEnd = trendAnalysis.Primary1H.Timestamp,
                PriceVs200EMA = primary.PriceVs200EMA,
                RSI = primary.RSI,
                ATRRatio = CalculateATRRatio(trendAnalysis, klines), // Use the new method
                VolumeRatio = CalculateVolumeRatio(trendAnalysis)
            };

            // 1. Check volatility first (overrides other classifications)
            if (regime.ATRRatio > 2.0m) // Only extreme volatility overrides
            {
                regime.Type = MarketRegimeType.HighVolatility;
                regime.Volatility = VolatilityLevel.VeryHigh;
            }
            // 2. Check for bullish conditions
            else if (primary.IsAlignedBullish && primary.PriceVs200EMA > 0 && primary.RSI > 40)
            {
                regime.Type = MarketRegimeType.BullishTrend;
                regime.TrendStrength = CalculateTrendStrength(trendAnalysis);
                regime.Volatility = CalculateVolatilityLevel(regime.ATRRatio);
            }
            // 3. Check for bearish conditions
            else if (primary.IsAlignedBearish && primary.PriceVs200EMA < 0 && primary.RSI < 60)
            {
                regime.Type = MarketRegimeType.BearishTrend;
                regime.TrendStrength = CalculateTrendStrength(trendAnalysis);
                regime.Volatility = CalculateVolatilityLevel(regime.ATRRatio);
            }
            // 4. Default to ranging market
            else
            {
                regime.Type = MarketRegimeType.RangingMarket;
                regime.TrendStrength = TrendStrength.Neutral;
                regime.Volatility = CalculateVolatilityLevel(regime.ATRRatio);
            }

            regime.TrendConfidence = CalculateTrendConfidence(trendAnalysis);
            regime.VolatilityConfidence = CalculateVolatilityConfidence(trendAnalysis);
            regime.OverallConfidence = (regime.TrendConfidence + regime.VolatilityConfidence) / 2;

            return regime;
        }

        // DYNAMIC TIMEFRAME MAPPING
        private (string primaryTF, string secondaryTF) GetAnalysisTimeframes(string tradingTimeframe)
        {
            return tradingTimeframe.ToLower() switch
            {
                "1m" => ("15m", "1h"),   // 1-min trades: 15m trend, 1h context
                "5m" => ("1h", "4h"),    // 5-min trades: 1h trend, 4h context  
                "15m" => ("4h", "1d"),   // 15-min trades: 4h trend, daily context
                "1h" => ("1d", "3d"),    // 1-hour trades: daily trend, 3-day context
                _ => ("1h", "4h")        // default fallback
            };
        }

        private TimeSpan GetSegmentDuration(string tradingTimeframe)
        {
            return tradingTimeframe.ToLower() switch
            {
                "1m" => TimeSpan.FromHours(2),   // 2-hour segments for 1m trading
                "5m" => TimeSpan.FromHours(4),   // 4-hour segments for 5m trading
                "15m" => TimeSpan.FromHours(6),  // 6-hour segments for 15m trading
                "1h" => TimeSpan.FromHours(12),  // 12-hour segments for 1h trading
                _ => TimeSpan.FromHours(4)       // default
            };
        }

        private async Task<BTCTrendAnalysis> GetBTCTrendAnalysisAsync(string symbol, DateTime startTime, DateTime endTime, string primaryTF, string secondaryTF)
        {
            var analysis = new BTCTrendAnalysis();

            try
            {
                // Fetch klines
                var klinesPrimary = await FetchKlines(symbol, primaryTF, startTime, endTime, 1000);
                var klinesSecondary = await FetchKlines(symbol, secondaryTF, startTime, endTime, 500);

                if (!klinesPrimary.Any())
                {
                    _logger.LogWarning("No primary kline data for {Symbol} {TF} {Start}-{End}",
                        symbol, primaryTF, startTime, endTime);
                    return analysis;
                }

                // Calculate indicators
                analysis.Primary1H = CalculateIndicatorSet(klinesPrimary, primaryTF);

                if (analysis.Primary1H == null)
                {
                    _logger.LogWarning("Failed to calculate primary indicators for {Symbol}", symbol);
                    return analysis;
                }

                // Calculate secondary indicators if available
                if (klinesSecondary.Any())
                {
                    analysis.Secondary4H = CalculateIndicatorSet(klinesSecondary, secondaryTF);
                }

                // Determine dominant trend timeframe
                analysis.DominantTrendTimeframe = DetermineDominantTimeframe(analysis);
                analysis.CompositeTrendScore = CalculateCompositeTrendScore(analysis);

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get BTC trend analysis for {Symbol} {PrimaryTF}/{SecondaryTF}",
                    symbol, primaryTF, secondaryTF);
                return analysis;
            }
        }
        private decimal CalculateATRRatio(BTCTrendAnalysis analysis, List<Kline> klines)
        {
            if (analysis?.Primary1H?.ATR == 0 || klines == null || klines.Count < 34)
            {
                _logger.LogWarning("Insufficient data for ATR ratio calculation: {KlineCount} klines", klines?.Count ?? 0);
                return 1.0m;
            }

            try
            {
                // Calculate multiple ATR values for moving average
                var atrValues = BTCTrendCalculator.CalculateATRValues(klines, 14, 20);
                
                if (atrValues.Any())
                {
                    var atrMA = atrValues.Average();
                    var currentATR = analysis.Primary1H.ATR;
                    var ratio = currentATR / atrMA;
                    
                    _logger.LogDebug("ATR Ratio: Current={CurrentATR}, MA={ATRMA}, Ratio={Ratio}", 
                        currentATR, atrMA, ratio);
                    
                    return ratio;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate ATR ratio with {KlineCount} klines", klines.Count);
            }
            
            return 1.0m;
        }

        private decimal CalculateVolumeRatio(BTCTrendAnalysis analysis)
        {
            return analysis.Primary1H?.Volume > 0 ? analysis.Primary1H.Volume / 1000000m : 1.0m;
        }

        private TrendStrength CalculateTrendStrength(BTCTrendAnalysis analysis)
        {
            var primary = analysis.Primary1H;
            if (primary == null) return TrendStrength.Neutral;

            var score = 0;

            // 1. EMA Alignment (strongest signal)
            if (primary.IsAlignedBullish) score += 3;
            if (primary.IsAlignedBearish) score -= 3;

            // 2. Price vs 200EMA strength
            if (Math.Abs(primary.PriceVs200EMA) > 10) score += 2;
            else if (Math.Abs(primary.PriceVs200EMA) > 5) score += 1;
            else if (Math.Abs(primary.PriceVs200EMA) < 1) score -= 1;

            // 3. RSI momentum
            if (primary.RSI > 70 || primary.RSI < 30) score += 2;
            else if (primary.RSI > 60 || primary.RSI < 40) score += 1;

            // 4. Multi-timeframe confirmation
            if (analysis.Secondary4H != null &&
                Math.Sign(primary.PriceVs200EMA) == Math.Sign(analysis.Secondary4H.PriceVs200EMA))
                score += 2;

            return score switch
            {
                >= 5 => TrendStrength.VeryStrong,
                >= 3 => TrendStrength.Strong,
                >= 1 => TrendStrength.Moderate,
                >= -2 => TrendStrength.Weak,
                _ => TrendStrength.Neutral
            };
        }

        private VolatilityLevel CalculateVolatilityLevel(decimal atrRatio)
        {
            return atrRatio switch
            {
                > 2.0m => VolatilityLevel.VeryHigh,
                > 1.5m => VolatilityLevel.High,
                > 1.2m => VolatilityLevel.Elevated,  // NEW
                > 0.8m => VolatilityLevel.Normal,
                _ => VolatilityLevel.Low
            };
        }

        private int CalculateTrendConfidence(BTCTrendAnalysis analysis)
        {
            var confidence = 50;
            if (analysis.Secondary4H != null &&
                Math.Sign(analysis.Primary1H.PriceVs200EMA) == Math.Sign(analysis.Secondary4H.PriceVs200EMA))
                confidence += 25;

            if (analysis.Primary1H?.RSI is > 60 or < 40)
                confidence += 15;

            if (analysis.Primary1H?.EMASpread is > 5 or < -5)
                confidence += 10;

            return Math.Min(confidence, 100);
        }

        private int CalculateVolatilityConfidence(BTCTrendAnalysis analysis)
        {
            return analysis.Primary1H?.ATR > 0 ? 75 : 50;
        }

        private string DetermineDominantTimeframe(BTCTrendAnalysis analysis)
        {
            if (analysis.Secondary4H != null &&
                Math.Sign(analysis.Primary1H.PriceVs200EMA) == Math.Sign(analysis.Secondary4H.PriceVs200EMA))
                return analysis.Secondary4H.Timeframe;

            return analysis.Primary1H.Timeframe;
        }

        private decimal CalculateCompositeTrendScore(BTCTrendAnalysis analysis)
        {
            var score = 50m;
            if (analysis.Primary1H != null)
            {
                score += (analysis.Primary1H.PriceVs200EMA / 4m);
                score += ((analysis.Primary1H.RSI - 50) / 3m);
                if (analysis.Primary1H.IsAlignedBullish) score += 10;
                if (analysis.Primary1H.IsAlignedBearish) score -= 10;
            }
            return Math.Max(0, Math.Min(100, score));
        }

        private decimal CalculateWinRate(List<Trade> trades)
        {
            if (trades.Count == 0) return 0;
            return (decimal)trades.Count(t => t.Profit > 0) / trades.Count * 100;
        }

        private async Task<List<Kline>> FetchKlines(string symbol, string interval, DateTime startTime, DateTime endTime, int limit = 1000)
        {
            var request = new RestRequest("/fapi/v1/klines", Method.Get);
            request.AddParameter("symbol", symbol, ParameterType.QueryString);
            request.AddParameter("interval", interval, ParameterType.QueryString);
            request.AddParameter("limit", limit.ToString(), ParameterType.QueryString);

            // KEY FIX: Fetch enough historical data for proper indicator calculation
            // We need at least 201 candles BEFORE our analysis period starts
            var hoursPerCandle = GetHoursPerCandle(interval);
            var hoursNeeded = 250 * hoursPerCandle; // 250 candles for buffer
            var analysisStartTime = startTime.AddHours(-hoursNeeded);

            request.AddParameter("startTime", new DateTimeOffset(analysisStartTime).ToUnixTimeMilliseconds(), ParameterType.QueryString);
            request.AddParameter("endTime", new DateTimeOffset(endTime).ToUnixTimeMilliseconds(), ParameterType.QueryString);

            var response = await _client.ExecuteGetAsync(request);

            if (response.IsSuccessful && response.Content != null)
            {
                var klines = ParseKlines(response.Content, symbol);
                _logger.LogInformation("Fetched {Count} {Interval} klines for {Symbol} (needed {Candles} candles)",
                    klines.Count, interval, symbol, 250);
                return klines;
            }
            else
            {
                _logger.LogWarning("Failed to fetch klines for {Symbol}: {Error}", symbol, response.ErrorMessage);
                return new List<Kline>();
            }
        }

        private double GetHoursPerCandle(string interval)
        {
            return interval.ToLower() switch
            {
                "1m" => 1 / 60.0,
                "5m" => 5 / 60.0,
                "15m" => 15 / 60.0,
                "1h" => 1,
                "4h" => 4,
                "1d" => 24,
                _ => 1
            };
        }

        private List<Kline> ParseKlines(string content, string symbol)
        {
            try
            {
                var klinesList = JsonConvert.DeserializeObject<List<List<object>>>(content);
                if (klinesList == null) return new List<Kline>();

                var klines = new List<Kline>();

                foreach (var klineData in klinesList)
                {
                    if (klineData.Count < 9) continue;

                    var kline = new Kline
                    {
                        Symbol = symbol,
                        OpenTime = Convert.ToInt64(klineData[0]),
                        Open = ParseDecimal(klineData[1]),
                        High = ParseDecimal(klineData[2]),
                        Low = ParseDecimal(klineData[3]),
                        Close = ParseDecimal(klineData[4]),
                        Volume = ParseDecimal(klineData[5]),
                        CloseTime = Convert.ToInt64(klineData[6]),
                        NumberOfTrades = Convert.ToInt32(klineData[8])
                    };

                    klines.Add(kline);
                }

                return klines;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing klines for {Symbol}", symbol);
                return new List<Kline>();
            }
        }

        private decimal ParseDecimal(object value)
        {
            if (value == null) return 0;

            string stringValue = value.ToString();

            if (decimal.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                return result;
            }

            return 0;
        }

        private BTCIndicatorSet CalculateIndicatorSet(List<Kline> klines, string timeframe)
        {
            try
            {
                if (!klines.Any() || klines.Count < 50) // Reduced minimum requirement
                {
                    _logger.LogWarning("Insufficient klines: {Count} (need at least 50)", klines.Count);
                    return null;
                }

                var latestKline = klines.Last();

                // Use all klines for calculation (they now include sufficient history)
                var closes = klines.Select(c => c.Close).ToList();

                var ema200 = CalculateEMA(closes, 200);
                var ema100 = CalculateEMA(closes, 100);
                var ema50 = CalculateEMA(closes, 50);

                return new BTCIndicatorSet
                {
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(latestKline.CloseTime).UtcDateTime,
                    Timeframe = timeframe,
                    Price = latestKline.Close,
                    EMA50 = ema50,
                    EMA100 = ema100,
                    EMA200 = ema200,
                    RSI = CalculateRSI(closes),
                    ATR = CalculateATR(klines),
                    Volume = klines.Average(k => k.Volume)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating indicators for {Timeframe}", timeframe);
                return null;
            }
        }

        private decimal CalculateRSI(List<decimal> prices, int period = 14)
        {
            return BTCTrendCalculator.CalculateRSI(prices, period);
        }

        private decimal CalculateATR(List<Kline> klines, int period = 14)
        {
            return BTCTrendCalculator.CalculateATR(klines, period);
        }

        private decimal CalculateEMA(List<decimal> prices, int period)
        {
            return BTCTrendCalculator.CalculateEMA(prices, period);
        }

        public async Task<List<MarketRegimeSegment>> GetRegimeSegmentsAsync(
            DateTime startTime, DateTime endTime, string tradingTimeframe, string symbol = "BTCUSDT")
        {
            var segments = new List<MarketRegimeSegment>();
            var (primaryTF, secondaryTF) = GetAnalysisTimeframes(tradingTimeframe);

            // Dynamic segment duration based on trading timeframe
            var segmentDuration = GetSegmentDuration(tradingTimeframe); // CHANGED: Use trading TF, not primary TF
            var currentSegmentStart = startTime;

            while (currentSegmentStart < endTime)
            {
                var currentSegmentEnd = currentSegmentStart + segmentDuration;
                if (currentSegmentEnd > endTime) currentSegmentEnd = endTime;

                try
                {
                    // Analyze THIS segment only
                    var segmentAnalysis = await GetBTCTrendAnalysisAsync(symbol, currentSegmentStart, currentSegmentEnd, primaryTF, secondaryTF);
                    // Get klines for this segment to calculate proper ATR ratio
                    var segmentKlines = await FetchKlines(symbol, primaryTF, currentSegmentStart, currentSegmentEnd, 100);
                    var regime = AnalyzeCurrentRegime(segmentAnalysis, segmentKlines); // Pass klines here

                    segments.Add(new MarketRegimeSegment
                    {
                        StartTime = currentSegmentStart,
                        EndTime = currentSegmentEnd,
                        Regime = regime,
                        TradeCount = 0, // We'll populate this later
                        TotalPnL = 0,   // We'll populate this later
                        WinRate = 0     // We'll populate this later
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to analyze segment {Start}-{End}", currentSegmentStart, currentSegmentEnd);
                    
                    segments.Add(new MarketRegimeSegment
                    {
                        StartTime = currentSegmentStart,
                        EndTime = currentSegmentEnd,
                        Regime = new MarketRegime { Type = MarketRegimeType.Unknown }
                    });
                }

                currentSegmentStart = currentSegmentEnd;
            }

            return segments;
        }
    }
}