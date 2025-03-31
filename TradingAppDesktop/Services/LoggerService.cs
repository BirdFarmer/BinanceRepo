using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace TradingAppDesktop.Services
{
    public class LoggerService : ILogger
    {
        private readonly MainWindow _mainWindow;
        private readonly string _categoryName;

        public LoggerService(MainWindow mainWindow, string categoryName)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        }

        public IDisposable BeginScope<TState>(TState state) => default!;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, 
                              Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"{DateTime.Now:T} [{logLevel}] {_categoryName}: {formatter(state, exception)}";
            
            _mainWindow.Dispatcher.Invoke(() =>
            {
                if (_mainWindow.Log != null)
                {
                    _mainWindow.LogText.AppendText(message + Environment.NewLine);
                    
                    // Fixed: Using static method call
                    var scrollViewer = MainWindow.FindVisualParent<ScrollViewer>(_mainWindow.LogText);
                    scrollViewer?.ScrollToEnd();
                }
            });
            
            Debug.WriteLine(message);
        }
    }
}