using System.Collections.Generic;
using System.Threading.Tasks;
using BinanceTestnet.Models;

namespace BinanceTestnet.Strategies
{
    // Implement this on strategies that can accept a pre-fetched snapshot
    public interface ISnapshotAwareStrategy
    {
        Task RunAsyncWithSnapshot(string symbol, string interval, Dictionary<string, List<Kline>> snapshot);
    }
}
