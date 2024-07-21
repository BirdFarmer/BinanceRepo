// StrategyRunner.cs

using BinanceLive.Models;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BinanceLive.Strategies
{
    public class StrategyRunner
    {
        private readonly List<string> _symbols;
        public string _interval;
        private readonly RestClient _client;
        private readonly string _apiKey;
        private readonly Wallet _wallet;
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
            var strategies = new List<StrategyBase>();

            // Add strategies based on the selected strategy
            if (_selectedStrategy == SelectedTradingStrategy.SMAExpansion || _selectedStrategy == SelectedTradingStrategy.Both)
            {
                strategies.Add(new SMAExpansionStrategy(_client, _apiKey, _orderManager, _wallet));
            }

            if (_selectedStrategy == SelectedTradingStrategy.MACD || _selectedStrategy == SelectedTradingStrategy.Both)
            {
                strategies.Add(new MACDDivergenceStrategy(_client, _apiKey, _orderManager, _wallet));
            }

            var tasks = new List<Task>();

            foreach (var symbol in _symbols)
            {
                foreach (var strategy in strategies)
                {
                    tasks.Add(strategy.RunAsync(symbol, _interval));
                }
            }

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
            var strategies = new List<StrategyBase>();

            // Add strategies based on the selected strategy
            if (_selectedStrategy == SelectedTradingStrategy.SMAExpansion || _selectedStrategy == SelectedTradingStrategy.Both)
            {
                strategies.Add(new SMAExpansionStrategy(_client, _apiKey, _orderManager, _wallet));
            }

            if (_selectedStrategy == SelectedTradingStrategy.MACD || _selectedStrategy == SelectedTradingStrategy.Both)
            {
                strategies.Add(new MACDDivergenceStrategy(_client, _apiKey, _orderManager, _wallet));
            }

            foreach (var strategy in strategies)
            {
                await strategy.RunOnHistoricalDataAsync(historicalData);
            }

            // Get the closing price of the last Kline
            var lastKline = historicalData.Last();
            var closePrice = lastKline.Close;

            // Close all active trades with the last Kline's close price
            _orderManager.CloseAllActiveTrades(closePrice);
        }

        private class PriceResponse
        {
            public decimal Price { get; set; }
        }
    }

}
