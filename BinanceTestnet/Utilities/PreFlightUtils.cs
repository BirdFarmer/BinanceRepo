using System;

namespace BinanceTestnet.Utilities
{
    public static class PreFlightUtils
    {
        // Determine if a market is "choppy" based on efficiency and minimum candles
        public static bool IsChoppy(decimal efficiency, int candlesCount, decimal threshold = 0.10m, int minCandles = 100)
        {
            if (candlesCount < minCandles) return false;
            return efficiency < threshold;
        }

        // Volume warning level: 0 = none, 1 = orange (low), 2 = red (critical)
        public static int GetVolumeWarningLevel(decimal volRatio)
        {
            if (volRatio <= 0) return 2; // treat missing/zero as critical
            if (volRatio < 0.3m) return 2;
            if (volRatio < 0.7m) return 1;
            return 0;
        }

        // Map price distance to ATRs into stage labels
        public static string GetStageLabel(decimal price, decimal reference, decimal atr)
        {
            if (atr <= 0) return "N/A";
            var distance = Math.Abs(price - reference);
            var distanceInATRs = distance / atr;

            if (distanceInATRs < 1m) return "Early";
            if (distanceInATRs <= 2m) return "Mid";
            return "Extended (High Risk)";
        }

        // Return a percent-like stage progress for UI (0-100)
        public static decimal GetStagePct(decimal price, decimal reference, decimal atr)
        {
            if (atr <= 0) return 0m;
            var distance = Math.Abs(price - reference);
            var distanceInATRs = distance / atr;
            // Map 0..2+ ATRs to 0..100
            var pct = Math.Min(200m, distanceInATRs * 100m);
            return Math.Max(0m, Math.Min(100m, pct));
        }
    }
}
