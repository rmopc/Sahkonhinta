using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Sahkonhinta_App.Services
{
    public class DayPriceData
    {
        public DateTime Date { get; set; }

        // Hourly average prices (default view)
        public List<Price> HourlyPrices { get; set; }

        // 15-minute interval prices (detailed view)
        public List<Price> FifteenMinutePrices { get; set; }

        // Backward compatibility: Prices returns hourly averages by default
        public List<Price> Prices => HourlyPrices;
    }

    public class PriceDataService
    {
        private const string API_URL = "https://oljemark.net/electricity_prices.json";

        // Shared for the lifetime of the app. HttpClient is documented as safe for
        // concurrent use across threads (MainPage's UI thread and the widget's
        // WorkManager background thread both call into this class). Reusing one
        // instance avoids per-call socket/TLS handshake overhead.
        // Deliberately NOT passing a custom HttpClientHandler here: the Android project
        // sets AndroidHttpClientHandlerType to Xamarin.Android.Net.AndroidClientHandler
        // so that new HttpClient() uses Android's native TLS stack (needed for modern
        // cert chains); constructing our own HttpClientHandler would silently override
        // that with Mono's managed handler and break HTTPS to the API.
        private static readonly HttpClient httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Short-lived in-memory cache so forceRefresh:false calls within a few minutes
        // of each other (e.g. MainPage re-appearing) skip the network fetch + parse.
        private static readonly object _cacheLock = new object();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
        private static DateTime _cacheTimestampUtc = DateTime.MinValue;
        private static DateTime _cacheTodayLocalDate = DateTime.MinValue;
        private static DayPriceData _cachedToday;
        private static DayPriceData _cachedTomorrow;

        public static async Task<(DayPriceData today, DayPriceData tomorrow)> GetPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);
                var todayLocal = nowLocal.Date;
                var tomorrowLocal = todayLocal.AddDays(1);

                if (!forceRefresh)
                {
                    lock (_cacheLock)
                    {
                        if (_cachedToday != null
                            && DateTime.UtcNow - _cacheTimestampUtc < CacheTtl
                            && _cacheTodayLocalDate == todayLocal)
                        {
                            Console.WriteLine("Returning cached price data");
                            return (_cachedToday, _cachedTomorrow);
                        }
                    }
                }

                var fetchStopwatch = Stopwatch.StartNew();
                var jsonObject = await FetchPriceDataFromApiAsync();
                fetchStopwatch.Stop();
                Console.WriteLine($"Price data fetch took {fetchStopwatch.ElapsedMilliseconds} ms");

                if (jsonObject == null)
                {
                    Console.WriteLine("Failed to fetch price data from API");
                    return (null, null);
                }

                // Parse the single "prices" array from the new API format
                var pricesArray = jsonObject["prices"] as JArray;
                if (pricesArray == null)
                {
                    Console.WriteLine("API response missing 'prices' array");
                    return (null, null);
                }

                // Parse only today's/tomorrow's entries in a single pass over the array
                // (the array may contain many months of history; see ParseTodayAndTomorrowFromArray)
                var parseStopwatch = Stopwatch.StartNew();
                var todayDatePrefix = todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var tomorrowDatePrefix = tomorrowLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var (todayFifteenMinPrices, tomorrowFifteenMinPrices) =
                    ParseTodayAndTomorrowFromArray(pricesArray, todayDatePrefix, tomorrowDatePrefix);
                parseStopwatch.Stop();
                Console.WriteLine($"Price data parse took {parseStopwatch.ElapsedMilliseconds} ms ({pricesArray.Count} total entries in response)");

                // Generate hourly averages from 15-minute data
                var todayHourlyPrices = GenerateHourlyAverages(todayFifteenMinPrices, localTimeZone);
                var tomorrowHourlyPrices = GenerateHourlyAverages(tomorrowFifteenMinPrices, localTimeZone);

                DayPriceData todayData = null;
                if (todayHourlyPrices.Any() || todayFifteenMinPrices.Any())
                {
                    todayData = new DayPriceData
                    {
                        Date = todayLocal,
                        HourlyPrices = todayHourlyPrices,
                        FifteenMinutePrices = todayFifteenMinPrices
                    };
                    Console.WriteLine($"Loaded today's data: {todayLocal:yyyy-MM-dd} ({todayHourlyPrices.Count} hourly, {todayFifteenMinPrices.Count} 15-min)");
                }

                DayPriceData tomorrowData = null;
                // Only consider tomorrow's prices available if we have more than 4 full hours
                // (not just the midnight hour 00:00-00:45, but actual data like 01:00, 02:00, etc.)
                if (tomorrowHourlyPrices.Count > 4)
                {
                    tomorrowData = new DayPriceData
                    {
                        Date = tomorrowLocal,
                        HourlyPrices = tomorrowHourlyPrices,
                        FifteenMinutePrices = tomorrowFifteenMinPrices
                    };
                    Console.WriteLine($"Loaded tomorrow's data: {tomorrowLocal:yyyy-MM-dd} ({tomorrowHourlyPrices.Count} hourly, {tomorrowFifteenMinPrices.Count} 15-min)");
                }
                else if (tomorrowHourlyPrices.Any())
                {
                    Console.WriteLine($"Tomorrow's data incomplete: only {tomorrowHourlyPrices.Count} hours available (need more than 4)");
                }

                if (todayData != null)
                {
                    lock (_cacheLock)
                    {
                        _cachedToday = todayData;
                        _cachedTomorrow = tomorrowData;
                        _cacheTimestampUtc = DateTime.UtcNow;
                        _cacheTodayLocalDate = todayLocal;
                    }
                }

                return (todayData, tomorrowData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPriceDataAsync: {ex.Message}");
                return (null, null);
            }
        }

        // Walks the (potentially many-months-long) prices array once and keeps only
        // entries matching today's or tomorrow's local date, parsing/allocating only
        // for those. Each entry's datetime string already carries the correct Helsinki
        // UTC offset for that instant, so its first 10 characters ("yyyy-MM-dd") are
        // already the correct Helsinki local calendar date - comparing that prefix is
        // equivalent to TimeZoneInfo.ConvertTimeFromUtc(...).Date but without paying for
        // a parse + timezone conversion on every discarded historical entry.
        private static (List<Price> today, List<Price> tomorrow) ParseTodayAndTomorrowFromArray(
            JArray priceArray, string todayDatePrefix, string tomorrowDatePrefix)
        {
            var today = new List<Price>();
            var tomorrow = new List<Price>();
            if (priceArray == null)
                return (today, tomorrow);

            foreach (var item in priceArray)
            {
                try
                {
                    var datetimeStr = item["datetime"]?.ToString();
                    if (string.IsNullOrEmpty(datetimeStr) || datetimeStr.Length < 10)
                        continue;

                    bool isToday = datetimeStr.StartsWith(todayDatePrefix, StringComparison.Ordinal);
                    bool isTomorrow = !isToday && datetimeStr.StartsWith(tomorrowDatePrefix, StringComparison.Ordinal);
                    if (!isToday && !isTomorrow)
                        continue;

                    // Use price_cents_kwh which is already converted to c/kWh
                    var priceCentsKwh = (double?)item["price_cents_kwh"];
                    if (!priceCentsKwh.HasValue)
                        continue;

                    var datetime = DateTimeOffset.Parse(datetimeStr);
                    var datetimeUtc = datetime.UtcDateTime;

                    // Convert back to the internal format (EUR/MWh * 10) for compatibility
                    // price_cents_kwh is in c/kWh, so multiply by 10 to get EUR/MWh * 10
                    var priceValue = priceCentsKwh.Value * 10;

                    var price = new Price { date = datetimeUtc, value = priceValue };
                    (isToday ? today : tomorrow).Add(price);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing price item: {ex.Message}");
                    continue;
                }
            }

            today.Sort((a, b) => a.date.CompareTo(b.date));
            tomorrow.Sort((a, b) => a.date.CompareTo(b.date));

            return (today, tomorrow);
        }

        private static List<Price> GenerateHourlyAverages(List<Price> fifteenMinutePrices, TimeZoneInfo localTimeZone)
        {
            var hourlyPrices = new List<Price>();

            if (!fifteenMinutePrices.Any())
                return hourlyPrices;

            // Group 15-minute prices by hour and calculate average
            var groupedByHour = fifteenMinutePrices
                .GroupBy(p =>
                {
                    var localTime = TimeZoneInfo.ConvertTimeFromUtc(p.date, localTimeZone);
                    return new DateTime(localTime.Year, localTime.Month, localTime.Day, localTime.Hour, 0, 0);
                })
                .OrderBy(g => g.Key);

            foreach (var hourGroup in groupedByHour)
            {
                var averagePrice = hourGroup.Average(p => p.value);

                // Convert the local hour back to UTC for storage
                var localHourStart = hourGroup.Key;
                var utcHourStart = TimeZoneInfo.ConvertTimeToUtc(localHourStart, localTimeZone);

                hourlyPrices.Add(new Price
                {
                    date = utcHourStart,
                    value = averagePrice
                });
            }

            return hourlyPrices;
        }

        private static async Task<JObject> FetchPriceDataFromApiAsync()
        {
            try
            {
                var response = await httpClient.GetAsync(API_URL);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrEmpty(json) || json.Length > 10000000)
                {
                    Console.WriteLine("Invalid API response: empty or too large");
                    return null;
                }

                // DateParseHandling.None keeps "datetime" values as plain strings exactly as
                // sent by the API (e.g. "2025-12-31T01:00:00+02:00"). Without this, JObject.Parse
                // auto-converts recognized date strings into DateTime-typed tokens, and reading
                // them back via ToString() formats using CultureInfo.CurrentCulture (fi-FI here,
                // set by MainActivity) instead of preserving the original ISO string/offset.
                JObject jsonObject;
                using (var stringReader = new System.IO.StringReader(json))
                using (var jsonTextReader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None })
                {
                    jsonObject = JObject.Load(jsonTextReader);
                }

                if (jsonObject?["prices"] == null)
                {
                    Console.WriteLine("Invalid API response: missing 'prices' array");
                    return null;
                }

                Console.WriteLine("Successfully fetched fresh price data from API");
                return jsonObject;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching price data from API: {ex.Message}");
                return null;
            }
        }

        // Compatibility method for widgets - returns raw JSON with both days combined
        // Uses hourly averages for cleaner widget display
        public static async Task<JObject> GetRawPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var (today, tomorrow) = await GetPriceDataAsync(forceRefresh);

                // Combine today and tomorrow hourly prices into a single list
                var allPrices = new List<Price>();

                if (today?.HourlyPrices != null)
                    allPrices.AddRange(today.HourlyPrices);

                if (tomorrow?.HourlyPrices != null)
                    allPrices.AddRange(tomorrow.HourlyPrices);

                // Create JSON object in the expected format
                var result = new JObject();
                result["prices"] = JArray.FromObject(allPrices);

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRawPriceDataAsync: {ex.Message}");
                return null;
            }
        }
    }

    public class Price
    {
        public DateTime date { get; set; }
        public double value { get; set; }
    }
}