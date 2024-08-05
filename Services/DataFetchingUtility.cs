using BinanceLive.Models;
using Newtonsoft.Json;
using RestSharp;
using System.Globalization;

namespace BinanceLive.Services
{
    public static class DataFetchingUtility
    {
    public static async Task<List<Kline>> FetchHistoricalData(RestClient client, string symbol, string interval)
    {
        var historicalData = new List<Kline>();
        var request = new RestRequest("/api/v3/klines", Method.Get);
        request.AddParameter("symbol", symbol);
        request.AddParameter("interval", interval);
        request.AddParameter("limit", 1000);

        var response = await client.ExecuteAsync<List<List<object>>>(request);

        if (response.IsSuccessful)
        {
            var klineData = JsonConvert.DeserializeObject<List<List<object>>>(response.Content);

            foreach (var kline in klineData)
            {
                historicalData.Add(new Kline
                {
                    OpenTime = (long)kline[0],
                    Open = decimal.Parse(kline[1].ToString(), CultureInfo.InvariantCulture),
                    High = decimal.Parse(kline[2].ToString(), CultureInfo.InvariantCulture),
                    Low = decimal.Parse(kline[3].ToString(), CultureInfo.InvariantCulture),
                    Close = decimal.Parse(kline[4].ToString(), CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(kline[5].ToString(), CultureInfo.InvariantCulture),
                    Symbol = symbol
                });
            }
        }
        else
        {
            Console.WriteLine($"Failed to fetch historical data for {symbol}: {response.ErrorMessage}");
        }

        return historicalData;
    }


        private static RestRequest CreateRequest(string resource, string apiKey)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("X-MBX-APIKEY", apiKey);
            return request;
        }

        private static void HandleErrorResponse(string symbol, RestResponse response)
        {
            Console.WriteLine($"Error for {symbol}: {response.ErrorMessage}");
            Console.WriteLine($"Status Code: {response.StatusCode}");
            Console.WriteLine($"Content: {response.Content}");
        }
    }
}