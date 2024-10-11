using BinanceTestnet.Models;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BinanceTestnet.Trading;
using System.Diagnostics;

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

        public async Task<Dictionary<string, decimal>> GetCurrentPricesAsync()
        {
            var currentPrices = new Dictionary<string, decimal>();

            foreach (var symbol in _symbols)
            {
                var request = new RestRequest($"/api/v3/ticker/price?symbol={symbol}");
                var response = await _client.ExecuteGetAsync<PriceResponse>(request);

                if (response.IsSuccessful && response.Data != null)
                {
                    currentPrices[symbol] = response.Data.Price;
                }
            }

            return currentPrices;
        }

        public async Task RunStrategiesOnHistoricalDataAsync(IEnumerable<Kline> historicalData)
        {
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
                _orderManager.CloseAllActiveTrades(closePrice);
            }

        }

        private List<StrategyBase> GetStrategies()
        {
            var strategies = new List<StrategyBase>();

            if (_selectedStrategy == SelectedTradingStrategy.SMAExpansion || _selectedStrategy == SelectedTradingStrategy.All)
            {
                //strategies.Add(new EmaVwapStrategy(_client, _apiKey, _orderManager, _wallet));
                strategies.Add(new SMAExpansionStrategy(_client, _apiKey, _orderManager, _wallet));
                //strategies.Add(new EmaStochRsiStrategy(_client, _apiKey, _orderManager, _wallet));
            }
            
            if (_selectedStrategy == SelectedTradingStrategy.MACD || _selectedStrategy == SelectedTradingStrategy.All)
            {
                strategies.Add(new EnhancedMACDStrategy(_client, _apiKey, _orderManager, _wallet));//MACDStandardStrategy
            }
            
            if (_selectedStrategy == SelectedTradingStrategy.Aroon || _selectedStrategy == SelectedTradingStrategy.All)
            {
                //strategies.Add(new AroonStrategy(_client, _apiKey, _orderManager, _wallet));
                strategies.Add(new HullSMAStrategy(_client, _apiKey, _orderManager, _wallet));
            }


            return strategies;
        }

        private class PriceResponse
        {
            public decimal Price { get; set; }
        }
    }
}
