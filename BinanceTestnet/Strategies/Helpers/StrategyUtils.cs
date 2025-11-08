using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RestSharp;
using BinanceTestnet.Models;
using Skender.Stock.Indicators;

namespace BinanceTestnet.Strategies.Helpers
{
    public static class StrategyUtils
    {
        // 1) HTTP
        public static RestRequest CreateGet(string resource, IDictionary<string, string>? query = null)
        {
            var request = new RestRequest(resource, Method.Get);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Accept", "application/json");
            if (query != null)
            {
                foreach (var kv in query)
                {
                    request.AddParameter(kv.Key, kv.Value, ParameterType.QueryString);
                }
            }
            return request;
        }

        // 2) Parsing
        public static bool TryParseKlines(string? content, out List<Kline> result)
        {
            result = new List<Kline>();
            if (string.IsNullOrWhiteSpace(content)) return false;
            try
            {
                var raw = Newtonsoft.Json.JsonConvert.DeserializeObject<List<List<object>>>(content);
                if (raw == null) return false;
                foreach (var k in raw)
                {
                    if (k.Count >= 9)
                    {
                        var open = SafeDecimal(k.ElementAtOrDefault(1));
                        var high = SafeDecimal(k.ElementAtOrDefault(2));
                        var low = SafeDecimal(k.ElementAtOrDefault(3));
                        var close = SafeDecimal(k.ElementAtOrDefault(4));
                        var openTime = ToLong(k.ElementAtOrDefault(0));
                        var closeTime = ToLong(k.ElementAtOrDefault(6));
                        var numberOfTrades = ToInt(k.ElementAtOrDefault(8));
                        var volume = SafeDecimal(k.ElementAtOrDefault(5));

                        result.Add(new Kline
                        {
                            Open = open,
                            High = high,
                            Low = low,
                            Close = close,
                            OpenTime = openTime,
                            CloseTime = closeTime,
                            NumberOfTrades = numberOfTrades,
                            Volume = volume
                        });
                    }
                }
                return result.Count > 0;
            }
            catch
            {
                result = new List<Kline>();
                return false;
            }
        }

        public static List<Kline> ParseKlines(string content)
        {
            return TryParseKlines(content, out var parsed) ? parsed : new List<Kline>();
        }

        // 3) Quotes conversion
        public static List<BinanceTestnet.Models.Quote> ToQuotes(IReadOnlyList<Kline> klines, bool includeOpen = true, bool includeVolume = true)
        {
            var list = new List<BinanceTestnet.Models.Quote>(klines.Count);
            foreach (var k in klines)
            {
                list.Add(new BinanceTestnet.Models.Quote
                {
                    Date = DateTimeOffset.FromUnixTimeMilliseconds(k.OpenTime).UtcDateTime,
                    Open = includeOpen ? k.Open : 0,
                    High = k.High,
                    Low = k.Low,
                    Close = k.Close,
                    Volume = includeVolume ? k.Volume : 0
                });
            }
            return list;
        }

        // Helper to build quotes for indicators, optionally excluding the forming candle
        public static List<BinanceTestnet.Models.Quote> ToIndicatorQuotes(IReadOnlyList<Kline> klines, bool useClosedCandle)
        {
            if (useClosedCandle)
            {
                var trimmed = ExcludeForming(klines);
                return ToQuotes(trimmed);
            }
            return ToQuotes(klines);
        }

        // Returns a copy of klines without the most recent (potentially forming) candle
        public static List<Kline> ExcludeForming(IReadOnlyList<Kline> klines)
        {
            if (klines == null || klines.Count == 0) return new List<Kline>();
            if (klines.Count == 1) return new List<Kline>();
            return klines.Take(klines.Count - 1).ToList();
        }

        // 4) Candle selection & guards
        public static bool HasEnough<T>(IReadOnlyList<T> list, int required) => list != null && list.Count >= required;

        public static (Kline? lastClosed, Kline? prevClosed) GetLastClosedPair(IReadOnlyList<Kline> klines)
        {
            if (!HasEnough(klines, 3)) return (null, null);
            return (klines[klines.Count - 2], klines[klines.Count - 3]);
        }

        // Choose the candle to evaluate signals on and its previous reference, based on policy
        public static (Kline? signal, Kline? previous) SelectSignalPair(IReadOnlyList<Kline> klines, bool useClosedCandle)
        {
            if (useClosedCandle)
            {
                if (!HasEnough(klines, 3)) return (null, null);
                return (klines[klines.Count - 2], klines[klines.Count - 3]);
            }
            else
            {
                if (!HasEnough(klines, 2)) return (null, null);
                return (klines[klines.Count - 1], klines[klines.Count - 2]);
            }
        }

        public static T? LastOrDefaultSafe<T>(IReadOnlyList<T> list) => list.Count > 0 ? list[list.Count - 1] : default;

        public static T? ElementAtOrDefaultSafe<T>(IReadOnlyList<T> list, int index)
        {
            return (index >= 0 && index < list.Count) ? list[index] : default;
        }

        // 5) Safe parsing helpers
        public static decimal SafeDecimal(object? value)
        {
            if (value == null) return 0m;
            return decimal.TryParse(value.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
        }

        private static long ToLong(object? value)
        {
            if (value == null) return 0L;
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); } catch { return 0L; }
        }

        private static int ToInt(object? value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        // 6) Moving average helpers (EHMA as implemented previously)
        public static List<(DateTime Date, decimal EHMA, decimal EHMAPrev)> CalculateEHMA(IReadOnlyList<BinanceTestnet.Models.Quote> quotes, int length)
        {
            var results = new List<(DateTime Date, decimal EHMA, decimal EHMAPrev)>(quotes.Count);
            if (length <= 0 || quotes.Count == 0)
            {
                foreach (var q in quotes)
                    results.Add((q.Date, 0m, 0m));
                return results;
            }

            var emaShort = quotes.GetEma(length / 2).ToList();
            var emaLong = quotes.GetEma(length).ToList();

            for (int i = 0; i < quotes.Count; i++)
            {
                if (i < length)
                {
                    results.Add((quotes[i].Date, 0m, 0m));
                    continue;
                }
                var ehma = (decimal)((emaShort[i].Ema ?? 0) * 2 - (emaLong[i].Ema ?? 0));
                var ehmaPrev = i > 0
                    ? (decimal)(((emaShort[i - 1].Ema ?? 0) * 2 - (emaLong[i - 1].Ema ?? 0)))
                    : ehma;
                results.Add((quotes[i].Date, ehma, ehmaPrev));
            }
            return results;
        }

        // 7) Order book helpers
        public static Dictionary<decimal, decimal> BucketOrders(List<List<decimal>> orders, int significantDigits = 4)
        {
            var dict = new Dictionary<decimal, decimal>();
            foreach (var row in orders)
            {
                if (row.Count < 2) continue;
                var price = row[0];
                var qty = row[1];
                var rounded = RoundToSignificantDigits(price, significantDigits);
                if (dict.ContainsKey(rounded)) dict[rounded] += qty; else dict[rounded] = qty;
            }
            return dict;
        }

        public static decimal RoundToSignificantDigits(decimal value, int significantDigits)
        {
            if (value == 0) return 0;
            var scale = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)Math.Abs(value))) + 1 - significantDigits);
            return Math.Round(value / scale) * scale;
        }
    }
}
