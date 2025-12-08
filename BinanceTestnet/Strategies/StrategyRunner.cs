using BinanceTestnet.Models;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Trading;
using System.Diagnostics;
using BinanceTestnet.Strategies;

namespace BinanceTestnet.Strategies
{
    public class StrategyRunner
    {
        private readonly List<string> _symbols;
        public string _interval;
        private readonly RestClient _client;
        private readonly string _apiKey;
        public Wallet _wallet;
        private readonly OrderManager _orderManager;
        private readonly List<SelectedTradingStrategy> _selectedStrategies;

        public StrategyRunner(RestClient client, string apiKey, List<string> symbols, 
                            string interval, Wallet wallet, OrderManager orderManager, 
                            List<SelectedTradingStrategy> selectedStrategies)
        {
            _client = client;
            _apiKey = apiKey;
            _symbols = symbols;
            _interval = interval;
            _wallet = wallet;
            _orderManager = orderManager;
            _selectedStrategies = selectedStrategies;
        }

        public async Task RunStrategiesAsync()
        {
            var strategies = GetStrategies();
            
            var tasks = new List<Task>();

            foreach (var symbol in _symbols)
            {
                foreach (var strategy in strategies)
                {
                    tasks.Add(strategy.RunAsync(symbol, _interval));
                }
            }

            // Ensure all strategies for all symbols complete before moving forward
            await Task.WhenAll(tasks);
        }

        public async Task RunStrategiesOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
            try{

                var strategies = GetStrategies();
                var lastKline = historicalData.Last();
                var closePrice = lastKline.Close;            

                foreach (var strategy in strategies)
                {
                    // Diagnostic: announce which strategy instance is about to run
                    Console.WriteLine($"Executing strategy object: {strategy.GetType().Name}");
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
                    await strategy.RunOnHistoricalDataAsync(historicalData);
                    var elapsed = timer.Elapsed;
                    Console.WriteLine($"----------Strategy {strategy.GetType().Name} lasted {elapsed} ");
                    _orderManager.CloseAllActiveTradesForBacktest(closePrice, lastKline.OpenTime);
                } 
            }
            catch(Exception)
            {
                // Swallowing exception intentionally; consider logging if needed.
            }

        }

        private List<StrategyBase> GetStrategies()
        {
            var strategies = new List<StrategyBase>(); 
    
            foreach (var strategyEnum in _selectedStrategies)
            {
                switch (strategyEnum)
                {
                    case SelectedTradingStrategy.EmaStochRsi:
                        strategies.Add(new EmaStochRsiStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.EnhancedMACD:
                        strategies.Add(new EnhancedMACDStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.FVG:
                        strategies.Add(new FVGStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.IchimokuCloud:
                        strategies.Add(new IchimokuCloudStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.CandleDistributionReversal:
                        strategies.Add(new CandleDistributionReversalStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.RSIMomentum:
                        strategies.Add(new RSIMomentumStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.MACDStandard:
                        strategies.Add(new MACDStandardStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.RsiDivergence:
                        strategies.Add(new RsiDivergenceStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.FibonacciRetracement:
                        strategies.Add(new FibonacciRetracementStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.Aroon:
                        strategies.Add(new AroonStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.HullSMA:
                        strategies.Add(new HullSMAStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.SMAExpansion:
                        strategies.Add(new SMAExpansionStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.BollingerNoSqueeze:
                        strategies.Add(new BollingerNoSqueezeStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;              
                    case SelectedTradingStrategy.SupportResistance:
                        strategies.Add(new SupportResistanceStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;              
                    case SelectedTradingStrategy.SimpleSMA375:
                        strategies.Add(new SimpleSMA375Strategy(_client, _apiKey, _orderManager, _wallet));
                        break;                        
                    case SelectedTradingStrategy.DEMASuperTrend:
                        strategies.Add(new DemaSupertrendStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;             
                    case SelectedTradingStrategy.CDVReversalWithEMA:
                        strategies.Add(new CDVReversalWithEMAStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                    case SelectedTradingStrategy.HarmonicPattern:
                        strategies.Add(new HarmonicPatternStrategy(_client, _apiKey, _orderManager, _wallet));
                        break;
                                                                        
                }
            }

            return strategies;
        }

        private List<StrategyBase> GetRandomStrategies(int numberOfStrategies)
        {
            // Get the full list of strategies
            var allStrategies = GetStrategies();

            // Shuffle the list of strategies
            var random = new Random();
            var shuffledStrategies = allStrategies.OrderBy(x => random.Next()).ToList();
            
            return shuffledStrategies.Take(numberOfStrategies).ToList();
        }


        private class PriceResponse
        {
            public decimal Price { get; set; }
        }
    }
}
