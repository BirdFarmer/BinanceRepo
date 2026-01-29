namespace BinanceTestnet.Strategies.Helpers
{
    // Runtime configuration toggled by UI or hosting app. Defaults are safe.
    public static class StrategyRuntimeConfig
    {
        // When true, strategies should use the last fully closed candle for signals.
        // When false, strategies may evaluate on the forming candle.
        public static bool UseClosedCandles { get; set; } = false;
        // When true, the runner will attempt to align to the timeframe boundary
        // before evaluating strategies (useful when using closed candles).
        public static bool AlignToBoundary { get; set; } = false;
        // When true, LondonSessionVolumeProfileStrategy should use session POC as an explicit stop-loss.
        // This value can be updated at runtime by the UI (SettingsWindow) so strategies pick it up
        // without requiring reloading from disk.
        public static bool LondonUsePocAsStop { get; set; } = true;
    }
}
