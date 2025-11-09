namespace BinanceTestnet.Strategies.Helpers
{
    // Runtime configuration toggled by UI or hosting app. Defaults are safe.
    public static class StrategyRuntimeConfig
    {
        // When true, strategies should use the last fully closed candle for signals.
        // When false, strategies may evaluate on the forming candle.
        public static bool UseClosedCandles { get; set; } = false;
    }
}
