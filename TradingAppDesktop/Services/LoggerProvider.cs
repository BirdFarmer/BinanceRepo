// Services/LoggerProvider.cs
using Microsoft.Extensions.Logging;
using System;
using System.Windows;

namespace TradingAppDesktop.Services
{
    public class LoggerProvider : ILoggerProvider
    {
        private readonly MainWindow _mainWindow;

        public LoggerProvider(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new LoggerService(_mainWindow, categoryName);
        }

        public void Dispose() { }
    }
}