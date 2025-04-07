using System;
using System.Diagnostics;
using System.Windows;
using BinanceTestnet.Enums;
using BinanceTestnet.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingAppDesktop.Services;

namespace TradingAppDesktop
{
    public partial class App : Application
    {
        private ILoggerFactory _loggerFactory;
        public BinanceTradingService TradingService { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 1. Create logger factory first
            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddDebug()
                    .AddProvider(new FileLoggerProvider(@"C:\Logs\app.log"))
                    .SetMinimumLevel(LogLevel.Debug);
            });

            // 2. Create main window
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            
            // 3. Initialize UI logger
            var uiLoggerProvider = new LoggerProvider(mainWindow);
            loggerFactory.AddProvider(uiLoggerProvider);
            
            // 4. Create trading service
            TradingService = new BinanceTradingService(
                loggerFactory.CreateLogger<BinanceTradingService>(), loggerFactory
            );
            
            // 5. Configure main window
            mainWindow.InitializeTradingParameters(
                OperationMode.LivePaperTrading,
                SelectedTradeDirection.Both,
                SelectedTradingStrategy.All,
                "5m", 20m, 15m, 5m
            );
            
            mainWindow.SetTradingService(TradingService);
            mainWindow.Show();
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