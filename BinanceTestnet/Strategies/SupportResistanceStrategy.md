# Support Resistance Breakout Strategy Documentation

## Overview
The Support Resistance Breakout Strategy is a technical analysis-based trading strategy that identifies key support/resistance levels and trades breakouts with volume confirmation and retest entries.

## Strategy Logic

### 1. Pivot Point Detection
- **Method**: Swing point detection using lookback period
- **Lookback**: Configurable (default: 20 candles)
- **Pivot Highs**: Highest point where both left and right candles have lower highs
- **Pivot Lows**: Lowest point where both left and right candles have higher lows

### 2. Level Selection
- **Resistance**: Highest pivot point BELOW current price
- **Support**: Lowest pivot point ABOVE current price

### 3. Breakout Detection
**Conditions for Valid Breakout:**
- Price closes beyond support/resistance level
- Volume exceeds 1.5x 20-period SMA (configurable)
- Breakout must be confirmed on closed candle only

### 4. Entry Logic (Retest Strategy)
**Long Entry (Resistance Breakout Retest):**
1. Resistance breakout confirmed
2. Current candle's low touches or retests broken resistance level
3. Previous candle was completely above resistance level
4. Retest occurs on low volume (< 100% volume SMA)

**Short Entry (Support Breakout Retest):**
1. Support breakout confirmed  
2. Current candle's high touches or retests broken support level
3. Previous candle was completely below support level
4. Retest occurs on low volume (< 100% volume SMA)

### 5. Risk Management
- **Breakout Invalidation**: If price returns completely beyond broken level
- **One Entry Per Breakout**: Prevents multiple entries on same level
- **Closed Candle Only**: All decisions based on completed candles

## Key Features

### Volume Analysis
- **Volume SMA**: 20-period simple moving average of volume
- **High Volume Threshold**: 1.5x SMA (breakout confirmation)
- **Low Volume Threshold**: 100% SMA (retest confirmation)

### Timeframe Agnostic
- Works on any timeframe (1m, 5m, 1h, 4h, etc.)
- Automatically adjusts timing based on selected interval
- Processes only completed candles

### Multi-Symbol Support
- Tracks breakout states independently per symbol
- Prevents over-trading with cooldown periods
- Handles multiple currency pairs simultaneously

## State Management

### Breakout States Tracked:
- `_resistanceBreakoutActive`: Active resistance breakout
- `_supportBreakoutActive`: Active support breakout  
- `_brokenResistanceLevel`: Price level of broken resistance
- `_brokenSupportLevel`: Price level of broken support
- `_entryTakenFrom...`: Prevents duplicate entries

### State Transitions:
1. **New Pivot Detected** → Reset breakout state for that level
2. **Breakout Occurs** → Activate breakout state, track broken level
3. **Retest Entry** → Mark entry as taken, maintain breakout state
4. **Invalidation** → Reset all states for that level

## Configuration Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `_lookback` | 20 | Pivot detection period |
| `_volumeMultiplier` | 1.5 | Volume spike multiplier |
| Processing Interval | 80% of candle period | Prevents over-processing |

## Algorithm Flow

Fetch Kline Data → Calculate Indicators → Detect Pivot Points → Identify S/R Levels
↓
Check for Breakouts → If Valid → Activate Breakout State → Wait for Retest
↓
Check Existing Breakouts → If Retest Conditions Met → Execute Trade
↓
Check Invalidation → Reset if Invalidated


## Advantages

1. **Clear Logic**: Simple support/resistance concept
2. **Volume Confirmation**: Reduces false breakouts
3. **Retest Entries**: Better risk-reward ratios
4. **Closed Candle Only**: Avoids premature entries
5. **Multi-Timeframe**: Adaptable to different trading styles

## Limitations

1. **Ranging Markets**: Performs poorly in sideways markets
2. **Pivot Sensitivity**: Lookback period affects level detection
3. **Volume Dependence**: Requires meaningful volume data
4. **Late Entries**: Retest approach may miss initial move

## Ideal Market Conditions

- **Trending markets** with clear support/resistance
- **High volatility** periods with volume spikes
- **Breakout continuation** patterns
- **Liquid symbols** with sufficient volume data

This strategy excels in markets with clear technical levels and follows the classic "break and retest" price action pattern favored by many professional traders.