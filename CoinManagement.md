# Coin Selection Management Guide

## Overview
The Coin Selection Management System allows you to manually select specific cryptocurrency pairs for trading, giving you precise control over which coins your trading bot monitors and trades.

## Accessing the Coin Selection Window
1. Click the "MANAGE COINS" button in the bottom-right corner of the main trading window
2. The Coin Selection Management window will open as a pop-up

## Selection Methods

### Auto-Select Tab
Choose from five automated selection methods:

**Top by Volume (High Liquidity)**
- Selects coins with highest trading volume
- Best for stable, liquid markets

**Top by Price Change (Volatility)**
- Picks coins with largest price movements
- Best for momentum strategies

**Top by Volume Change (Momentum)**
- Chooses coins with sudden volume spikes
- Best for catching emerging trends

**Composite Score (Volume + Price Change)**
- Balanced selection using multiple factors
- Best for general trading strategies

**Biggest Coins (High Market Cap)**
- Selects established, high-market-cap coins
- Best for conservative trading

### Manual Select Tab
Manually enter specific coin pairs:

**Format Examples:**
BTCUSDT, ETHUSDT, ADAUSDT
or
BTCUSDT
ETHUSDT
ADAUSDT

**Important Notes:**
- Only USDT pairs are supported
- Each coin must end with "USDT"
- Separate coins with commas or new lines

### Saved Lists Tab
Manage and load previously saved coin selections.

Capabilities:
- View all previously saved selections from the database
- Load a saved list into the preview panel and apply it to trading
- Delete a saved list from the database
- Refresh the list to pick up newly saved selections immediately

Notes:
- Saved items currently display their coin count and saved time range
- Lists are persisted in the app database and survive restarts
- Lists display their saved name

## How to Use

### Using Auto-Select:
1. Choose your selection method from the dropdown
2. Enter the number of coins (default: 80)
3. Click "Generate Selection"
4. Review selected coins in the preview panel
5. Click "Apply to Trading" to save

### Using Manual Select:
1. Enter your desired coin pairs in the text box
2. Click "Apply Manual Selection"
3. Review the selection in the preview panel
4. Click "Apply to Trading" to save

## Preview Panel Features
- Selected coins count
- Selection method used
- Last updated timestamp
- Scrollable list of all selected coins

## Applying Your Selection
1. Make your coin selection using any method
2. Review in the preview panel
3. Click "APPLY TO TRADING" (green button)
4. You'll see a confirmation message
5. Close the window or make additional changes

## What Happens After Applying
- Your selection is saved to the database
- The main trading system is notified
- Your next trading session will use ONLY these coins
- The selection persists until you change it
- The selection now remains active across multiple runs; it is no longer cleared after a session completes

## Quick Actions
- **Apply to Trading**: Saves and applies current selection
- **Save Current List**: Saves selection without immediate application (appears in Saved Lists immediately)
- **Load Saved List**: Loads the highlighted saved list into the preview panel
- **Delete Saved List**: Removes the highlighted saved list from the database
- **Refresh Lists**: Reloads the Saved Lists from the database
- **Clear Selection**: Removes all selected coins
- **Close**: Exits without saving changes

## Best Practices

### For Beginners:
- Start with 10-20 coins
- Use "Biggest Coins" for stability
- Gradually experiment with different methods

### Strategy Recommendations:
- **Day Trading**: Use "Top by Price Change" or "Volume Change"
- **Swing Trading**: Use "Composite Score" or "Biggest Coins"
- **High-Frequency**: Use "Top by Volume" for liquidity

## Troubleshooting

### Common Issues:
**Selection Not Applied**
- Ensure you clicked "APPLY TO TRADING"
- Check for confirmation message
- Verify coins appear in preview panel

**Auto-Selection Still Running**
- Custom selections apply only to NEXT trading session
- Start new trading session after applying changes

**Invalid Coins Error**
- Ensure all coins end with "USDT"
- Remove spaces or special characters
- Check for typos in coin symbolsf

**Saved List Not Showing Up**
- Click "Refresh Lists" in the Saved Lists tab
- Ensure you clicked "Save Current List" after generating or entering your selection
- Verify the app is pointing to the expected database file (shown in the Saved Lists tab)

**Cannot Load/Delete a List**
- Select a list in the Saved Lists tab before clicking Load or Delete
- If the list was deleted externally, click Refresh to update the view

## Important Notes
- Custom selections apply only to new trading sessions
- Selections are saved between app restarts
- If no custom selection exists, system uses automatic selection
- Changing selections won't affect currently running sessions

## Developer notes
- Database methods added in `BinanceTestnet/Database/DatabaseManager.cs`:
	- `GetAllCoinPairLists()` — returns all saved lists (Id, pairs, start/end)
	- `GetCoinPairListById(int id)` — returns coin pairs for a specific list
	- `DeleteCoinPairList(int id)` — deletes a saved list
- UI wiring in `TradingAppDesktop/Views/CoinSelectionWindow.xaml.cs`:
	- Populates Saved Lists on open and after saving
	- Load and Delete handlers operate on the selected list
	- Saving a list now immediately refreshes the Saved Lists panel


## Changelog
- 2025-10-30:
	- Saved Lists are fully functional: list, load, delete, and refresh
	- Saving a list immediately updates the Saved Lists view
	- Custom coin selections persist across runs (no longer cleared after a session)

## Verification
After applying your selection:
1. Check main window logs for confirmation
2. Start trading session and verify only selected coins are traded
3. Monitor trading activity to confirm expected behavior