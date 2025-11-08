using System;

namespace BinanceTestnet.Strategies.Helpers
{
    public enum CandleSelectionMode
    {
        Forming,
        Closed
    }

    public static class CandlePolicy
    {
        private static CandleSelectionMode? _mode;

        // Determine policy at runtime from environment variable TRADING_USE_CLOSED_CANDLES (true/false)
        public static CandleSelectionMode Mode
        {
            get
            {
                if (_mode.HasValue) return _mode.Value;
                var env = Environment.GetEnvironmentVariable("TRADING_USE_CLOSED_CANDLES");
                if (!string.IsNullOrWhiteSpace(env) && bool.TryParse(env, out var useClosed) && useClosed)
                {
                    _mode = CandleSelectionMode.Closed;
                }
                else
                {
                    _mode = CandleSelectionMode.Forming; // default preserves current live behavior
                }
                return _mode.Value;
            }
            set { _mode = value; }
        }

        public static bool UseClosed => Mode == CandleSelectionMode.Closed;
    }
}
