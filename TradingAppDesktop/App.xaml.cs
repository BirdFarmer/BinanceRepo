using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BinanceTestnet.Enums;
using BinanceTestnet.Trading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingAppDesktop.Controls;
using TradingAppDesktop.Services;

namespace TradingAppDesktop
{
    public partial class App : Application
    {
    public BinanceTradingService? TradingService { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            // Removed CandlePolicy; strategies use forming candle implicitly.

            var (isApproved, deviceId) = await HardwareLockService.CheckApprovalAsync();
            
            if (!isApproved)
            {
                // Create a selectable TextBox dialog
                var dialog = new Window {
                    Title = "Activation Required",
                    Width = 400,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                var textBox = new TextBox {
                    Text = deviceId,
                    IsReadOnly = true,
                    FontSize = 14,
                    Margin = new Thickness(10),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    TextWrapping = TextWrapping.Wrap
                };

                var button = new Button {
                    Content = "Copy to Clipboard",
                    Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                button.Click += (s, args) => {
                    Clipboard.SetText(deviceId);
                    button.Content = "Copied!";
                };

                dialog.Content = new StackPanel {
                    Children = {
                        new TextBlock {
                            Text = HardwareLockService.GetActivationMessage(deviceId),
                            Margin = new Thickness(10),
                            TextWrapping = TextWrapping.Wrap
                        },
                        textBox,
                        button
                    }
                };

                dialog.ShowDialog();
                Environment.Exit(0);
                return;
            }

            // AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            // DispatcherUnhandledException += OnDispatcherUnhandledException;
            // TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            base.OnStartup(e);
    
            // 0. Crash Handler
            DispatcherUnhandledException += (sender, ex) => 
            {
                string crashLog = $"[{DateTime.Now}] CRASH:\n{ex.Exception}\n\n";
                File.AppendAllText(@"C:\Logs\app_crashes.log", crashLog);
                
                MessageBox.Show($"A critical error occurred:\n{ex.Exception.Message}", 
                            "Error", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                
                ex.Handled = true; // Set to false if you want the app to close
            };
                
            // 1. Create logger factory first
            var loggerFactory = LoggerFactory.Create(builder => {
                builder
                    .AddDebug()
                    .AddProvider(new FileLoggerProvider(@"C:\Logs\app.log"))
                    .SetMinimumLevel(LogLevel.Debug);
            });

            //2. Create main window
            MainWindow? mainWindow = null;

            try
            { 
                mainWindow = new MainWindow();
                MainWindow = mainWindow;
            }
            catch (Exception ex)
            {
                string crashLog = $"[{DateTime.Now}] CRASH DURING MainWindow CONSTRUCTION:\n{ex}\n\n";
                File.AppendAllText(@"C:\Logs\app_crashes.log", crashLog);

                MessageBox.Show($"A critical error occurred during startup:\n{ex.Message}", 
                                "Startup Error", 
                                MessageBoxButton.OK, 
                                MessageBoxImage.Error);

                Environment.Exit(1); // hard stop to prevent unstable state
                return;
            }
            
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
                "5m", 20m, 15m, 2.5m
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
        

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ShowFatalError((e.ExceptionObject as Exception) ?? new Exception("Unknown unhandled exception"));
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ShowFatalError(e.Exception);
            e.Handled = true;
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            ShowFatalError(e.Exception);
            e.SetObserved();
        }

        private void ShowFatalError(Exception ex)
        {
            File.WriteAllText("crash_log.txt", $"{ex.Message}\n\n{ex.StackTrace}");
            MessageBox.Show($"A critical error occurred:\n{ex.Message}", "Crash!", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1);  // Ensure app closes gracefully
        }


    }
}