# CDV Reversal with EMA50

Summary
- **Strategy name:** CDV Reversal with EMA50
- **Purpose:** Detect high-probability reversal points using Cumulative Delta Volume (CDV) divergence patterns confirmed by an EMA50 position and EMA crossover entry.
- **Modes supported:** Backtest, Paper Trade, Live Trade
- **Integration:** Uses existing order management and risk frameworks in the project. Generates trace logs via `StrategyUtils.TraceSignalCandle` and places orders via `OrderManager`.

Core concept
- The strategy looks for divergences between price candles and CDV candles (derived from trade-level buy/sell deltas). A "double" trigger identifies a reversal setup when price and CDV move in opposite directions with limited wick structure.
- An EMA50 is used to ensure the price is positioned at a likely reversal region (below EMA50 for bullish, above EMA50 for bearish). Entry is only executed when a whole candle crosses the EMA50 (confirmatory candle).

Trigger conditions
- Double Bullish Trigger (Long setup):
  - Price candle is GREEN (close > open)
  - CDV candle is RED (cumulative delta decreasing)
  - CDV candle has NO UPPER wick
  - Price high < EMA50 (price positioned near market bottom)

- Double Bearish Trigger (Short setup):
  - Price candle is RED (close < open)
  - CDV candle is GREEN (cumulative delta increasing)
  - CDV candle has NO LOWER wick
  - Price low > EMA50 (price positioned near market top)

Entry conditions
- Long Entry:
  - Whole candle crosses above EMA50 (current low > EMA50 and previous low <= EMA50)
  - Active bullish trigger present within the last 40 bars

- Short Entry:
  - Whole candle crosses below EMA50 (current high < EMA50 and previous high >= EMA50)
  - Active bearish trigger present within the last 40 bars

Trigger lifecycle & risk management
- Triggers expire after 40 bars if not used.
- Opposite triggers cancel each other (a new bullish trigger cancels an existing bearish trigger and vice versa).
- One trade per trigger; consumed triggers are not reused.
- Uses the project's `OrderManager` for entries/exits and `StrategyUtils.TraceSignalCandle` for trace logging.

Data requirements
- OHLCV candle series (closed-candle mode supported).
- Trade-level aggregated data (aggTrades) to compute CDV per candle; when unavailable, a signed-volume proxy is used for historical/backtest runs.
- EMA50 computed from candle close prices.

Implementation notes
- The strategy class was added as `BinanceTestnet/Strategies/CDVReversalWithEMAStrategy.cs` and registered in the strategy runner and UI selector.
- For real-time live runs, the implementation attempts to fetch `/fapi/v1/aggTrades` per candle to compute CDV precisely. If aggTrades cannot be fetched, it falls back to a signed-volume proxy computed from candle open/close and volume.
- Historical backtests use a simplified signed-volume cumulative proxy to recreate CDV candles.

How to use
- Enable the strategy in the Desktop app `Strategy Selector` as "CDV Reversal + EMA50".
- Choose timeframe (recommended: 5m, 15m, 1h) and run backtests or live runs.
- For best reproducibility, provide aggTrades access for symbol ranges when running live to compute accurate CDV.

Pine Script
```
//@version=5
indicator("CDV Double Triggers with Entries", "CDV Triggers & Entries")

// Basic inputs
linestyle = input.string(defval = 'Candle', title = "Style", options = ['Candle', 'Line'])
hacandle = input.bool(defval = true, title = "Heikin Ashi Candles?")
colorup = input.color(defval = color.lime, title = "Normal Up", inline = "bcol")
colordown = input.color(defval = color.red, title = "Normal Down", inline = "bcol")

// Trigger sensitivity
min_body_wick_ratio = input.float(defval = 1.5, title = "Min Body/Wick Ratio", minval = 1.0)
wick_tolerance = input.float(defval = 0.0001, title = "Wick Tolerance", minval = 0.0)
price_ema_len = input.int(defval = 50, title = "Price EMA Length", minval = 1)
trigger_lookback = input.int(defval = 40, title = "Trigger Lookback Period", minval = 1)

// Calculate CDV
_rate(cond) =>
    tw = high - math.max(open, close) 
    bw = math.min(open, close) - low 
    body = math.abs(close - open)
    ret = 0.5 * (tw + bw + (cond ? 2 * body : 0)) / (tw + bw + body) 
    ret := nz(ret) == 0 ? 0.5 : ret
    ret
    
deltaup = volume * _rate(open <= close) 
deltadown = volume * _rate(open > close)
delta = close >= open ? deltaup : -deltadown
cumdelta = ta.cum(delta)

// Calculate CDV candles
float o = na
float h = na
float l = na
float c = na
if linestyle == 'Candle'
    o := cumdelta[1]
    h := math.max(cumdelta, cumdelta[1])
    l := math.min(cumdelta, cumdelta[1])
    c := cumdelta

// Heikin Ashi calculations
float haclose = na
float haopen = na
float hahigh = na
float halow = na
haclose := (o + h + l + c) / 4
haopen := na(haopen[1]) ? (o + c) / 2 : (haopen[1] + haclose[1]) / 2
hahigh := math.max(h, math.max(haopen, haclose))
halow := math.min(l, math.min(haopen, haclose))

// Final CDV values
c_ = hacandle ? haclose : c
o_ = hacandle ? haopen : o
h_ = hacandle ? hahigh : h
l_ = hacandle ? halow : l

// Calculate price EMA for validation
price_ema = ta.ema(close, price_ema_len)

// Calculate CDV candle properties
cdv_body_size = math.abs(c_ - o_)
cdv_upper_wick = h_ - math.max(o_, c_)
cdv_lower_wick = math.min(o_, c_) - l_

// CORRECTED Trigger conditions with EMA validation:
// SHORT trigger (bearish):
bearish_trigger = close < open and c_ > o_ and cdv_lower_wick <= wick_tolerance and cdv_body_size > cdv_upper_wick * min_body_wick_ratio and low > price_ema

// LONG trigger (bullish):
bullish_trigger = close > open and c_ < o_ and cdv_upper_wick <= wick_tolerance and cdv_body_size > cdv_lower_wick * min_body_wick_ratio and high < price_ema

// Double trigger conditions (two in a row)
double_bullish_trigger = bullish_trigger and bullish_trigger[1]
double_bearish_trigger = bearish_trigger and bearish_trigger[1]

// Active trigger tracking
var int active_bullish_trigger_bar = na
var int active_bearish_trigger_bar = na

// Update active triggers with invalidation rules
if double_bullish_trigger
    active_bullish_trigger_bar := bar_index
    // Opposite trigger invalidates
    active_bearish_trigger_bar := na

if double_bearish_trigger
    active_bearish_trigger_bar := bar_index
    // Opposite trigger invalidates
    active_bullish_trigger_bar := na

// Check if triggers are still active (within lookback AND not expired)
is_active_bullish_trigger = not na(active_bullish_trigger_bar) and (bar_index - active_bullish_trigger_bar) <= trigger_lookback
is_active_bearish_trigger = not na(active_bearish_trigger_bar) and (bar_index - active_bearish_trigger_bar) <= trigger_lookback

// Auto-invalidate triggers after 40 bars
if not na(active_bullish_trigger_bar) and (bar_index - active_bullish_trigger_bar) > trigger_lookback
    active_bullish_trigger_bar := na

if not na(active_bearish_trigger_bar) and (bar_index - active_bearish_trigger_bar) > trigger_lookback
    active_bearish_trigger_bar := na

// Entry conditions
// LONG entry: Whole candle crossed above EMA50 AND active bullish trigger
long_entry = low > price_ema and low[1] <= price_ema[1] and is_active_bullish_trigger

// SHORT entry: Whole candle crossed below EMA50 AND active bearish trigger  
short_entry = high < price_ema and high[1] >= price_ema[1] and is_active_bearish_trigger

// Deactivate triggers on entry
if long_entry
    active_bullish_trigger_bar := na

if short_entry
    active_bearish_trigger_bar := na

// Determine candle colors - keep normal colors
final_color = c_ >= o_ ? colorup : colordown
final_border = c_ >= o_ ? colorup : colordown
final_wick = c_ >= o_ ? colorup : colordown

// Plot candles with normal colors
plotcandle(o_, h_, l_, c_, title='CDV Candles', 
          color = final_color, 
          bordercolor = final_border, 
          wickcolor = final_wick)

// Background colors:
// - Orange: Double bullish triggers
// - Purple: Double bearish triggers  
// - Green: Long entries
// - Red: Short entries
bgcolor(double_bullish_trigger ? color.new(color.orange, 90) : double_bearish_trigger ? color.new(color.purple, 90) : long_entry ? color.new(color.green, 80) : short_entry ? color.new(color.red, 80) : na)
```

Change log
- Created initial implementation and UI registration on branch 112-fvg-strategy-core-improvements---remove-order-book-add-trend-filters.

Notes & next steps
- I added a best-effort CDV builder that calls Binance aggTrades; verify rate limits and consider batching or caching for performance.
- Optionally add more filters (volume confirmation, momentum) and TP/SL presets accessible from the UI.

