// StrategyRunner.cs

using RestSharp;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BinanceLive.Strategies
{
    public class StrategyRunner
    {
        private readonly List<string> _symbols;
        private readonly string _interval;
        private readonly RestClient _client;
        private readonly string _apiKey;
        private readonly Wallet _wallet;
        private readonly OrderManager _orderManager;

        public StrategyRunner(RestClient client, string apiKey, List<string> symbols, string interval, Wallet wallet, OrderManager orderManager)
        {
            _client = client;
            _apiKey = apiKey;
            _symbols = symbols;
            _interval = interval;
            _wallet = wallet;
            _orderManager = orderManager;
        }

        public async Task RunStrategiesAsync(params StrategyBase[] strategies)
        {
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

        private class PriceResponse
        {
            public decimal Price { get; set; }
        }
    }
}
