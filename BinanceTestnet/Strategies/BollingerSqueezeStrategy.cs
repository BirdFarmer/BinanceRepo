using RestSharp;
using BinanceTestnet.Trading;
using System;

namespace BinanceTestnet.Strategies
{
    [Obsolete("Use BollingerNoSqueezeStrategy in BollingerNoSqueezeStrategy.cs instead")]
    public class BollingerSqueezeStrategy : BollingerNoSqueezeStrategy
    {
        public BollingerSqueezeStrategy(RestClient client, string apiKey, OrderManager orderManager, Wallet wallet)
            : base(client, apiKey, orderManager, wallet)
        {
        }
    }
}