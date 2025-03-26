using System;
using System.Diagnostics;
using System.Windows;
using BinanceTestnet.Enums;
using BinanceTestnet.Trading;
using TradingAppDesktop.Services;

namespace TradingAppDesktop
{
    public partial class App : Application
    {
        public BinanceTradingService TradingService { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize trading service first
            TradingService = new BinanceTradingService();
            ConfigureConsoleHandling();

            // Create and configure main window
            MainWindow = new MainWindow();
            
            // Initialize with default values
            ((MainWindow)MainWindow).InitializeTradingParameters(
                OperationMode.LivePaperTrading,
                SelectedTradeDirection.Both,
                SelectedTradingStrategy.All,
                "5m",
                20m,
                15m,
                5m
            );
            
            MainWindow.Show();
            base.OnStartup(e);
        }

        private void ConfigureConsoleHandling()
        {
            try
            {
                Console.TreatControlCAsInput = false;
                Console.CancelKeyPress += (sender, args) => 
                {
                    args.Cancel = true;
                    ShutdownGracefully();
                };
            }
            catch
            {
                // Console not available in pure GUI mode
            }
        }

        private void ShutdownGracefully()
        {
            Dispatcher.Invoke(() =>
            {
                TradingService?.StopTrading(true);
                Current.Shutdown();
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Nuclear option if standard shutdown fails
            if (TradingService?.IsRunning == true)
            {
                Process.GetCurrentProcess().Kill();
            }
            base.OnExit(e);
        }
    }
}