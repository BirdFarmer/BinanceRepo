using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using BinanceTestnet.Models;

namespace BinanceTestnet.Services
{
    public class HistoricalDataLoader
    {
        public List<Kline> LoadHistoricalData(string filePath)
        {
            var jsonData = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<Kline>>(jsonData);
        }
    }
}
