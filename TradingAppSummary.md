# Crypto Trading Application - Technical Summary

## Overview
A comprehensive C#-based cryptocurrency trading application that supports multiple trading modes, strategies, and futures trading operations (long/short positions) across various coin pairs.

## Core Features

### 🎯 Trading Modes
- **Back Testing** - Historical strategy testing with configurable date ranges
- **Live Paper Trading** - Real-time simulation without actual capital  
- **Real Live Trading** - Live execution with real funds

### 📊 Trading Configuration

#### Position Management
- **Direction**: Long and/or Short positions
- **Entry Size**: Custom position sizing
- **Leverage**: Adjustable leverage for futures trading
- **Timeframe**: 1 minute to 4 hour candles

#### Risk Management
- **Take Profit**: ATR-based multiplier for dynamic profit targets
- **Stop Loss**: Risk ratio divider relative to take profit
- **Max Data**: 1000 candles limit for backtesting

#### Exit Management (TP, SL, Trailing)
- Exit Modes: Take Profit or Trailing Stop (Trailing replaces TP)
- Stop Loss is always present and honored first
- Trailing Stop specifics:
  - Live: server-side `TRAILING_STOP_MARKET` (reduceOnly), direction-aware activation price, callback clamped to [0.1%, 5.0%]
  - Paper/Backtest: simulated activation, peak/trough tracking, and callback retrace exit
  - In trailing mode, SL is derived from Activation % and Risk-Reward divider: `slDistance = (activation% × entry) ÷ RR`
- Details and examples: see `ExitManagement.md`

### 🔄 Execution Engine

#### Multi-Pair Processing
- Loops through multiple cryptocurrency pairs
- Concurrent strategy evaluation
- Futures contract management

#### Strategy Framework
- **13 Available Strategies** - Multiple trading algorithms to choose from
- **1-5 Active Strategies** - User-selectable maximum of 5 simultaneous strategies
- Strategy rotation across pairs  
- Configurable strategy parameters
 - Availability note: Candle Distribution is Real-only in this app. It is disabled (greyed out) in Live Paper and Backtest with a tooltip: "Real-only strategy (uses order book data). Switch to Live Real to enable."

### 🖥️ Desktop UI Enhancements
- Recent Trade Entries panel:
	- Shows recent trade entries (scrolls as new entries arrive)
	- Color-coded: green for long, red for short
	- Displays symbol, strategy, entry price, and timestamp (UTC; weekday names forced to English)
	- Clears at the start of each new session
	- Fed by the trading service via a lightweight view model

### 🧪 Paper Wallet (Paper Trading mode only)
- Equity card at the top-right of the log pane, above the Recent Trades list
- Updates once per cycle, not on every tick
- Resets to a fresh baseline at the start of each Paper session
- Header shows session start time, right-aligned (e.g., “started 01 Nov 14:35 UTC”)
- Metrics shown:
  - Equity (Free + Used + Unrealized)
  - Realized PnL (session) = (Free + Used) − Starting
  - Unrealized PnL (sum over active trades)
  - Used (sum of initial margins)
  - Free (wallet balance)
  - Active (open trades)

### 🧼 UX Polish
- Disabled buttons are visually greyed out for clarity
- Status tag in Recent Trades header shows REAL / PAPER / BACKTEST (IDLE by default)
- Recent Trades entries in live mode are added only after a successful order response (no pre-execution noise). Failures/negative paths don’t produce entries; local entry price is used for display.

### 🧩 Coin Selection Management
- Manual and automated control over the trading universe (USDT pairs)
- Auto-select methods: Top by Volume, Top by Price Change, Top by Volume Change, Composite Score, Biggest Coins
- Adjustable coin limit (e.g., top 80)
- Manual entry of coin pairs (e.g., BTCUSDT, ETHUSDT)
- Named Saved Lists: save, view, load, delete, and refresh lists directly in the UI
- Preview panel shows method, count, timestamp, and selected coins
- Apply to Trading updates the next session’s universe
- Selection persists across sessions and app restarts (no longer cleared after runs)

## Reporting & Analytics

### Automated Reporting
- **Back Test Reports**: Comprehensive HTML performance report upon completion
- **Paper Trade Reports**: Detailed HTML analysis after simulation periods
- **Interactive Analysis**: Executive summaries, market regime analysis, and actionable insights
- **Performance Metrics**: Trade analytics, equity curves, and strategy statistics
- **Visual Charts**: Graphical representation of trading results and portfolio performance

### Advanced Report Features
- **Executive Summary**: Top insights and performance overview
- **Market Context Analysis**: BTC regime alignment and timing patterns
- **Realistic Simulation**: 8-trade limit impact analysis
- **Session Performance**: Market hours and weekend/weekday analysis
- **Risk Analysis**: Critical trades and liquidation monitoring
- **Strategy Comparison**: Multi-strategy performance breakdown
- **Coin Performance**: Top performers and underperformers

## Technical Specifications

### Input Parameters
- **Trading Mode**: BackTest, PaperTrade, or LiveTrade
- **Position Direction**: Long, Short, or Both
- **Entry Size**: Position sizing amount
- **Leverage**: Futures trading leverage multiplier
- **Strategy Selection**: Choose 1-5 strategies from 13 available options
- **Timeframe**: 1 minute to 4 hour intervals
- **Take Profit**: ATR multiplier for profit targets
- **Stop Loss**: Risk ratio divider relative to take profit
- **Backtest Range**: Start and end datetime (max 1000 candles)
- **Symbol Universe**: Choose via auto-select method, manual list, or load a named Saved List; USDT pairs only; configurable coin count limit

### Data Constraints
- **Maximum Backtest Range**: 1000 candles
- **Real-time Data**: Live market feeds from Binance
- **Historical Data**: OHLCV + technical indicators

## Architecture Highlights

- **C# Implementation** - High-performance, type-safe backend
- **Modular Strategy System** - Pluggable trading algorithms
- **Risk Management Layer** - Isolated position and risk controls
- **Binance Integration** - Current exchange connection with multi-exchange architecture potential
- **Real-time Processing** - Low-latency execution engine
- **HTML Reporting** - Automated performance visualization and analytics
- **Market Analysis** - BTC context and regime detection for timing optimization
- **UI Data Flow** - Trading service publishes trade-entry events to a `RecentTradesViewModel` (WPF), which binds to the Recent Trades list in `MainWindow`
- **Paper Wallet ViewModel** - `PaperWalletViewModel` backs the Equity card; resets at Paper session start and updates once per cycle using current prices and active trades
- **App Data Store** - Single-file SQLite database `TradingData.db` in the application directory for selections, logs, and reports metadata
- **Persistent Universe Store** - Named saved lists stored in the app database with UI load/delete and automatic refresh after save

## Use Cases

1. **Strategy Development** - Backtest and refine trading algorithms with detailed HTML reports
2. **Risk Assessment** - Paper trade to validate strategy performance before live deployment
3. **Live Execution** - Automated trading with real capital on Binance
4. **Portfolio Diversification** - Multi-pair, multi-strategy approach
5. **Performance Analysis** - Comprehensive reporting for strategy optimization
6. **Market Analysis** - BTC context and trading pattern analysis

---

*Built for professional crypto traders seeking automated, systematic trading on Binance futures markets with comprehensive analytics and reporting capabilities.*