using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xamarin.Essentials;

namespace Sahkonhinta_App.Services
{
    public class DayPriceData
    {
        public DateTime Date { get; set; }
        public List<Price> Prices { get; set; }
        public bool IsComplete => Prices != null && Prices.Count == 24;
    }

    public class PriceDataService
    {
        private const string CACHE_KEY_TODAY_DATA = "CachedTodayPriceData";
        private const string CACHE_KEY_TODAY_DATE = "CachedTodayDate";
        private const string CACHE_KEY_TOMORROW_DATA = "CachedTomorrowPriceData";
        private const string CACHE_KEY_TOMORROW_DATE = "CachedTomorrowDate";
        private const string CACHE_KEY_LAST_FETCH = "LastFetchTimestamp";

        public static async Task<(DayPriceData today, DayPriceData tomorrow)> GetPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);
                var todayLocal = nowLocal.Date;
                var tomorrowLocal = todayLocal.AddDays(1);

                // Check cached dates
                var cachedTodayDateStr = Preferences.Get(CACHE_KEY_TODAY_DATE, "");
                var cachedTomorrowDateStr = Preferences.Get(CACHE_KEY_TOMORROW_DATE, "");
                var lastFetchTime = Preferences.Get(CACHE_KEY_LAST_FETCH, 0L);

                // Check if midnight has passed and we need to rotate data
                bool needsRotation = !string.IsNullOrEmpty(cachedTomorrowDateStr) &&
                                     cachedTomorrowDateStr == todayLocal.ToString("yyyy-MM-dd");

                if (needsRotation)
                {
                    RotateTomorrowToToday();
                    cachedTodayDateStr = Preferences.Get(CACHE_KEY_TODAY_DATE, "");
                    cachedTomorrowDateStr = Preferences.Get(CACHE_KEY_TOMORROW_DATE, "");
                }

                // Load cached data
                DayPriceData cachedToday = LoadCachedDay(CACHE_KEY_TODAY_DATA, cachedTodayDateStr, todayLocal);
                DayPriceData cachedTomorrow = LoadCachedDay(CACHE_KEY_TOMORROW_DATA, cachedTomorrowDateStr, tomorrowLocal);

                bool haveTodayData = cachedToday != null && cachedToday.IsComplete;
                bool haveTomorrowData = cachedTomorrow != null;

                // Determine if we should fetch fresh data
                bool shouldFetch = forceRefresh || !haveTodayData;

                // After 14:00 Finnish time, check for tomorrow's data once per hour if we don't have it
                if (!shouldFetch && nowLocal.Hour >= 14 && !haveTomorrowData)
                {
                    var timeSinceLastFetch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lastFetchTime;
                    var oneHourInMs = 60 * 60 * 1000;
                    if (timeSinceLastFetch > oneHourInMs)
                    {
                        shouldFetch = true;
                    }
                }

                if (shouldFetch)
                {
                    var (fetchedToday, fetchedTomorrow) = await FetchAndCachePriceDataAsync(todayLocal, tomorrowLocal);

                    if (fetchedToday != null || fetchedTomorrow != null)
                    {
                        // Use fetched data if available, otherwise keep cached
                        cachedToday = fetchedToday ?? cachedToday;
                        cachedTomorrow = fetchedTomorrow ?? cachedTomorrow;
                    }
                }

                // If we still don't have today's data, try one more fetch without caching constraints
                if (cachedToday == null || !cachedToday.IsComplete)
                {
                    var (fetchedToday, fetchedTomorrow) = await FetchAndCachePriceDataAsync(todayLocal, tomorrowLocal);
                    cachedToday = fetchedToday ?? cachedToday;
                    cachedTomorrow = fetchedTomorrow ?? cachedTomorrow;
                }

                return (cachedToday, cachedTomorrow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPriceDataAsync: {ex.Message}");

                // Try to return cached data as fallback
                var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki")).Date;
                var tomorrowLocal = todayLocal.AddDays(1);

                var cachedTodayDateStr = Preferences.Get(CACHE_KEY_TODAY_DATE, "");
                var cachedTomorrowDateStr = Preferences.Get(CACHE_KEY_TOMORROW_DATE, "");

                var cachedToday = LoadCachedDay(CACHE_KEY_TODAY_DATA, cachedTodayDateStr, todayLocal);
                var cachedTomorrow = LoadCachedDay(CACHE_KEY_TOMORROW_DATA, cachedTomorrowDateStr, tomorrowLocal);

                return (cachedToday, cachedTomorrow);
            }
        }

        private static void RotateTomorrowToToday()
        {
            try
            {
                // Move tomorrow's data to today's slot
                var tomorrowData = Preferences.Get(CACHE_KEY_TOMORROW_DATA, "");
                var tomorrowDate = Preferences.Get(CACHE_KEY_TOMORROW_DATE, "");

                if (!string.IsNullOrEmpty(tomorrowData) && !string.IsNullOrEmpty(tomorrowDate))
                {
                    Preferences.Set(CACHE_KEY_TODAY_DATA, tomorrowData);
                    Preferences.Set(CACHE_KEY_TODAY_DATE, tomorrowDate);

                    // Clear tomorrow's cache
                    Preferences.Remove(CACHE_KEY_TOMORROW_DATA);
                    Preferences.Remove(CACHE_KEY_TOMORROW_DATE);

                    Console.WriteLine($"Rotated tomorrow's data to today: {tomorrowDate}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error rotating data: {ex.Message}");
            }
        }

        private static DayPriceData LoadCachedDay(string dataKey, string cachedDateStr, DateTime expectedDate)
        {
            try
            {
                if (string.IsNullOrEmpty(cachedDateStr) || cachedDateStr != expectedDate.ToString("yyyy-MM-dd"))
                    return null;

                var jsonData = Preferences.Get(dataKey, "");
                if (string.IsNullOrEmpty(jsonData))
                    return null;

                var dayData = JsonConvert.DeserializeObject<DayPriceData>(jsonData);
                return dayData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading cached day: {ex.Message}");
                return null;
            }
        }

        private static async Task<(DayPriceData today, DayPriceData tomorrow)> FetchAndCachePriceDataAsync(DateTime todayDate, DateTime tomorrowDate)
        {
            try
            {
                var jsonObject = await FetchRawPriceDataFromApiAsync();
                if (jsonObject == null)
                    return (null, null);

                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var prices = jsonObject["prices"];
                var jsonArray = JArray.Parse(prices.ToString());
                var allPrices = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());

                // Extract today's prices (full 24 hours from 00:00 to 23:00)
                var todayPrices = allPrices
                    .Where(x => TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Date == todayDate)
                    .OrderBy(x => x.date)
                    .ToList();

                // Extract tomorrow's prices (full 24 hours from 00:00 to 23:00)
                var tomorrowPrices = allPrices
                    .Where(x => TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Date == tomorrowDate)
                    .OrderBy(x => x.date)
                    .ToList();

                DayPriceData todayData = null;
                DayPriceData tomorrowData = null;

                // Cache today's data if we have any hours
                if (todayPrices.Any())
                {
                    todayData = new DayPriceData
                    {
                        Date = todayDate,
                        Prices = todayPrices
                    };

                    Preferences.Set(CACHE_KEY_TODAY_DATA, JsonConvert.SerializeObject(todayData));
                    Preferences.Set(CACHE_KEY_TODAY_DATE, todayDate.ToString("yyyy-MM-dd"));
                    Console.WriteLine($"Cached today's data: {todayDate:yyyy-MM-dd} ({todayPrices.Count} hours)");
                }

                // Cache tomorrow's data if we have any hours
                if (tomorrowPrices.Any())
                {
                    tomorrowData = new DayPriceData
                    {
                        Date = tomorrowDate,
                        Prices = tomorrowPrices
                    };

                    Preferences.Set(CACHE_KEY_TOMORROW_DATA, JsonConvert.SerializeObject(tomorrowData));
                    Preferences.Set(CACHE_KEY_TOMORROW_DATE, tomorrowDate.ToString("yyyy-MM-dd"));
                    Console.WriteLine($"Cached tomorrow's data: {tomorrowDate:yyyy-MM-dd} ({tomorrowPrices.Count} hours)");
                }

                Preferences.Set(CACHE_KEY_LAST_FETCH, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                return (todayData, tomorrowData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching and caching data: {ex.Message}");
                return (null, null);
            }
        }

        private static async Task<JObject> FetchRawPriceDataFromApiAsync()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    var response = await httpClient.GetAsync("https://sahkotin.fi/prices");
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(json) || json.Length > 10000000)
                    {
                        Console.WriteLine("Invalid API response: empty or too large");
                        return null;
                    }

                    var jsonObject = JObject.Parse(json);

                    if (jsonObject?["prices"] == null)
                    {
                        Console.WriteLine("Invalid API response: missing prices");
                        return null;
                    }

                    Console.WriteLine("Successfully fetched fresh price data from API");
                    return jsonObject;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching price data from API: {ex.Message}");
                return null;
            }
        }

        public static void ClearCache()
        {
            try
            {
                Preferences.Remove(CACHE_KEY_TODAY_DATA);
                Preferences.Remove(CACHE_KEY_TODAY_DATE);
                Preferences.Remove(CACHE_KEY_TOMORROW_DATA);
                Preferences.Remove(CACHE_KEY_TOMORROW_DATE);
                Preferences.Remove(CACHE_KEY_LAST_FETCH);
                Console.WriteLine("Price cache cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }

        // Compatibility method for widgets - returns raw JSON with both days combined
        public static async Task<JObject> GetRawPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var (today, tomorrow) = await GetPriceDataAsync(forceRefresh);

                // Combine today and tomorrow prices into a single list
                var allPrices = new List<Price>();

                if (today?.Prices != null)
                    allPrices.AddRange(today.Prices);

                if (tomorrow?.Prices != null)
                    allPrices.AddRange(tomorrow.Prices);

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
