using System.Windows;
using BinanceTestnet.Enums; 
using BinanceTestnet.Database;
using BinanceTestnet.Trading;
using BinanceLive.Strategies;
using BinanceTestnet.Models;
using TradingAppDesktop;
using TradingAppDesktop.Services;


public partial class App : Application
{
    private BinanceTradingService _tradingService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _tradingService = new BinanceTradingService();
        _tradingService.StartTrading(
            operationMode: OperationMode.LivePaperTrading,
            tradeDirection: SelectedTradeDirection.Both,
            selectedStrategy: SelectedTradingStrategy.All,
            interval: "5m",
            entrySize: 20m,
            leverage: 15m,
            takeProfit: 5m
        );

        // Start the main window
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tradingService?.StopTrading();
        base.OnExit(e);
    }
}