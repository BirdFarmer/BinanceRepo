using RestSharp;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace TradingAppDesktop.Services
{
    public class HealthChecker
    {
        private readonly RestClient _client;
        private readonly ILogger _logger;
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        public HealthChecker(RestClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckAllEndpointsAsync()
        {
            var result = new HealthCheckResult
            {
                PingHealthy = await CheckPingAsync(),
                ExchangeInfoHealthy = await CheckExchangeInfoAsync()
            };
            result.FullyOperational = result.PingHealthy && result.ExchangeInfoHealthy;
            return result;
        }

        private async Task<bool> CheckExchangeInfoAsync(int maxRetries = 2)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                using var cts = new CancellationTokenSource(_timeout);
                try
                {
                    _logger.LogDebug($"Checking exchange info (attempt {attempt}/{maxRetries})");
                    
                    var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);
                    var response = await _client.ExecuteAsync(request, cts.Token);

                    if (response.IsSuccessful)
                    {
                        // Additional validation - check if response contains expected data
                        if (!string.IsNullOrEmpty(response.Content) && 
                            response.Content.Contains("symbols") && 
                            response.Content.Contains("TRADING"))
                        {
                            return true;
                        }
                        _logger.LogWarning("Exchange info returned invalid data format");
                    }
                    else
                    {
                        _logger.LogWarning($"Exchange info check failed: {response.StatusCode}");
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    _logger.LogWarning($"Exchange info check timed out (attempt {attempt})");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Exchange info check error (attempt {attempt})");
                }

                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * attempt); // Exponential backoff
                }
            }
            return false;
        }

        private async Task<bool> CheckPingAsync()
        {
            try
            {
                var request = new RestRequest("/fapi/v1/ping", Method.Get);
                var response = await _client.ExecuteAsync(request);
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping check failed");
                return false;
            }
        }
    }

    public class HealthCheckResult
    {
        public bool PingHealthy { get; set; }
        public bool ExchangeInfoHealthy { get; set; }
        public bool FullyOperational { get; set; }
    }
}