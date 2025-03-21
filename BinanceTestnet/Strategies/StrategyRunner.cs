using BinanceTestnet.Models;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Trading;
using System.Diagnostics;
using BinanceTestnet.Strategies;

namespace BinanceLive.Strategies
{
    public class StrategyRunner
    {
        private readonly List<string> _symbols;
        public string _interval;
        private readonly RestClient _client;
        private readonly string _apiKey;
        public Wallet _wallet;
        private readonly OrderManager _orderManager;
        private readonly SelectedTradingStrategy _selectedStrategy;

        public StrategyRunner(RestClient client, string apiKey, List<string> symbols, string interval, Wallet wallet, OrderManager orderManager, SelectedTradingStrategy selectedStrategy)
        {
            _client = client;
            _apiKey = apiKey;
            _symbols = symbols;
            _interval = interval;
            _wallet = wallet;
            _orderManager = orderManager;
            _selectedStrategy = selectedStrategy;
        }

        public async Task RunStrategiesAsync()
        {
            var strategies = GetStrategies();
            // var strategies = GetRandomStrategies(3);
            
            //Console.WriteLine($"------------------Loop over 3 random strategies\n* {strategies[0].ToString()} *  \n* {strategies[1].ToString()} * \n* {strategies[2].ToString()} * " );
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
                    Stopwatch timer = new Stopwatch();
                    timer.Start();
                    await strategy.RunOnHistoricalDataAsync(historicalData);
                    var elapsed = timer.Elapsed;
                    Console.WriteLine($"------------------Strategy {strategy.ToString()} lasted {elapsed} " );
                    // Close all active trades with the last Kline's close price
                    _orderManager.CloseAllActiveTradesForBacktest(closePrice, lastKline.OpenTime);
                } 
            }
            catch(Exception e)
            {}

        }

        private List<StrategyBase> GetStrategies()
        {
            var strategies = new List<StrategyBase>();

            // strategies.Add(new CandleDistributionReversalStrategy(_client, _apiKey, _orderManager, _wallet));
            // strategies.Add(new EmaStochRsiStrategy(_client, _apiKey, _orderManager, _wallet));
            //  strategies.Add(new EnhancedMACDStrategy(_client, _apiKey, _orderManager, _wallet));
            //   strategies.Add(new FVGStrategy(_client, _apiKey, _orderManager, _wallet));
            // strategies.Add(new RSIMomentumStrategy(_client, _apiKey, _orderManager, _wallet));
            // strategies.Add(new SMAExpansionStrategy(_client, _apiKey, _orderManager, _wallet));
            //  strategies.Add(new MACDStandardStrategy(_client, _apiKey, _orderManager, _wallet));
            strategies.Add(new RsiDivergenceStrategy(_client, _apiKey, _orderManager, _wallet));
            // strategies.Add(new IchimokuCloudStrategy(_client, _apiKey, _orderManager, _wallet));         
            // strategies.Add(new FibonacciRetracementStrategy(_client, _apiKey, _orderManager, _wallet));        
            // strategies.Add(new AroonStrategy(_client, _apiKey, _orderManager, _wallet));
            // strategies.Add(new HullSMAStrategy(_client, _apiKey, _orderManager, _wallet));

            return strategies;
        }

        private List<StrategyBase> GetRandomStrategies(int numberOfStrategies)
        {
            // Get the full list of strategies
            var allStrategies = GetStrategies();

            // Shuffle the list of strategies
            var random = new Random();
            var shuffledStrategies = allStrategies.OrderBy(x => random.Next()).ToList();

            // Select the specified number of strategies
            
            return shuffledStrategies.Take(numberOfStrategies).ToList();
        }


        private class PriceResponse
        {
            public decimal Price { get; set; }
        }
    }
}
