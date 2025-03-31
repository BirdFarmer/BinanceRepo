// FileLoggerProvider.cs
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;

namespace TradingAppDesktop.Services
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _filePath;
        private readonly long _maxFileSize;
        private readonly int _maxRetainedFiles;

        public FileLoggerProvider(string path, long maxFileSize = 5_000_000, int maxRetainedFiles = 3)
        {
            _filePath = path;
            _maxFileSize = maxFileSize;
            _maxRetainedFiles = maxRetainedFiles;
            
            // Ensure log directory exists
            var logDir = Path.GetDirectoryName(path);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(_filePath, _maxFileSize, _maxRetainedFiles);
        }

        public void Dispose() { }

        private class FileLogger : ILogger
        {
            private readonly string _filePath;
            private readonly long _maxFileSize;
            private readonly int _maxRetainedFiles;
            private readonly object _lock = new object();

            public FileLogger(string path, long maxFileSize, int maxRetainedFiles)
            {
                _filePath = path;
                _maxFileSize = maxFileSize;
                _maxRetainedFiles = maxRetainedFiles;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, 
                                  Exception exception, Func<TState, Exception, string> formatter)
            {
                lock (_lock)
                {
                    RollFiles();
                    var message = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {formatter(state, exception)}{Environment.NewLine}";
                    File.AppendAllText(_filePath, message);
                }
            }

            private void RollFiles()
            {
                try
                {
                    var fileInfo = new FileInfo(_filePath);
                    if (fileInfo.Exists && fileInfo.Length > _maxFileSize)
                    {
                        // Rotate files
                        for (int i = _maxRetainedFiles - 1; i >= 0; i--)
                        {
                            var currentFile = i == 0 ? _filePath : $"{_filePath}.{i}";
                            var nextFile = $"{_filePath}.{i + 1}";
                            
                            if (File.Exists(currentFile))
                            {
                                File.Move(currentFile, nextFile, true);
                            }
                        }
                    }
                }
                catch { /* Don't crash if logging fails */ }
            }
        }
    }
}