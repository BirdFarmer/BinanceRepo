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

        public HealthChecker(RestClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckAllEndpointsAsync()
        {
            var result = new HealthCheckResult();
            
            result.PingHealthy = await CheckPingAsync();
            
            if (result.PingHealthy)
            {
                result.ExchangeInfoHealthy = await CheckExchangeInfoAsync();
            }

            return result;
        }

        private async Task<bool> CheckPingAsync()
        {
            try
            {
                var response = await _client.ExecuteAsync(new RestRequest("/fapi/v1/ping", Method.Get));
                return response.IsSuccessful;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ping check failed: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckExchangeInfoAsync()
        {
            try
            {
                var request = new RestRequest("/fapi/v1/exchangeInfo", Method.Get);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _client.ExecuteAsync(request, cts.Token);
                
                return response.IsSuccessful && response.Content?.Contains("symbols") == true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"ExchangeInfo check failed: {ex.Message}");
                return false;
            }
        }
    }

    public class HealthCheckResult
    {
        public bool PingHealthy { get; set; }
        public bool ExchangeInfoHealthy { get; set; }
        public bool FullyOperational => PingHealthy && ExchangeInfoHealthy;
    }
}