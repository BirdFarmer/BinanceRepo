using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;

public class VolatilityBasedTPandSL
{
    private const int AtrPeriod = 14;

    public static (decimal tpPercent, decimal slPercent) CalculateTpAndSl(string symbol, List<Quote> history, List<Quote> btcHistory, decimal takeProfitPercent)
    {
        // Ensure there is enough data
        if (history.Count < AtrPeriod || btcHistory.Count < AtrPeriod)
        {
            throw new ArgumentException("Not enough historical data to calculate ATR.");
        }

        // Calculate ATR for the target coin pair
        var atrs = history.GetAtr(AtrPeriod).Where(a => a.Atr.HasValue).Select(a => a.Atr.Value).ToList();
        var currentAtr = atrs.LastOrDefault();
        var currentPrice = history.Last().Close;
        
        var defaultTpPercent = takeProfitPercent;
        var defaultSlPercent = takeProfitPercent / 1.5M;

        if (currentAtr == 0 || currentPrice == 0)
        {
            throw new InvalidOperationException("Current ATR or price is zero, which is invalid for calculation.");
        }

        // Print ATR values for debugging
        Console.WriteLine($"Current ATR for {symbol}: {currentAtr}");
        Console.WriteLine($"Current Price for {symbol}: {currentPrice}");

        // Calculate ATR for BTCUSDT as the baseline
        var btcAtrs = btcHistory.GetAtr(AtrPeriod).Where(a => a.Atr.HasValue).Select(a => a.Atr.Value).ToList();
        var btcAtr = btcAtrs.LastOrDefault();
        var btcPrice = btcHistory.Last().Close;

        if (btcAtr == 0 || btcPrice == 0)
        {
            throw new InvalidOperationException("BTC ATR or price is zero, which is invalid for calculation.");
        }

        // Print BTC ATR values for debugging
        Console.WriteLine($"Current ATR for BTCUSDT: {btcAtr}");
        Console.WriteLine($"Current Price for BTCUSDT: {btcPrice}");

        // Normalize ATR by price
        var normalizedAtr = (decimal)currentAtr / currentPrice;
        var normalizedBtcAtr = (decimal)btcAtr / btcPrice;

        // Calculate ATR ratio
        var atrRatio = normalizedAtr / normalizedBtcAtr;
        Console.WriteLine($"ATR Ratio (Normalized Target / Normalized BTC): {atrRatio}");

        // Adjust TP and SL percentages based on ATR ratio
        var tpPercent = defaultTpPercent * atrRatio;
        var slPercent = defaultSlPercent * atrRatio;

        // Print TP and SL percentages for debugging
        Console.WriteLine($"Adjusted TP Percent: {tpPercent}");
        Console.WriteLine($"Adjusted SL Percent: {slPercent}");

        return (tpPercent, slPercent);
    }
}
