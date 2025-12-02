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
    public class PriceDataService
    {
        private const string CACHE_KEY_TODAY_DATE = "CachedTodayDate";
        private const string CACHE_KEY_TOMORROW_DATE = "CachedTomorrowDate";
        private const string CACHE_KEY_RAW_DATA = "CachedRawPriceData";
        private const string CACHE_KEY_LAST_FETCH = "LastFetchTimestamp";

        public static async Task<JObject> GetRawPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone).Date;
                var tomorrowLocal = todayLocal.AddDays(1);

                // Check if we have today's data cached
                var cachedTodayDate = Preferences.Get(CACHE_KEY_TODAY_DATE, "");
                var cachedTomorrowDate = Preferences.Get(CACHE_KEY_TOMORROW_DATE, "");
                var cachedRawData = Preferences.Get(CACHE_KEY_RAW_DATA, "");
                var lastFetchTime = Preferences.Get(CACHE_KEY_LAST_FETCH, 0L);

                bool haveTodayData = cachedTodayDate == todayLocal.ToString("yyyy-MM-dd") && !string.IsNullOrEmpty(cachedRawData);
                bool haveTomorrowData = cachedTomorrowDate == tomorrowLocal.ToString("yyyy-MM-dd");

                // Current hour in Finnish time
                var currentHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone).Hour;

                // Determine if we should fetch fresh data
                bool shouldFetch = forceRefresh || !haveTodayData;

                // After 14:00 Finnish time, check for tomorrow's data once per hour if we don't have it
                if (!shouldFetch && currentHour >= 14 && !haveTomorrowData)
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
                    // Fetch fresh data from API
                    var freshData = await FetchRawPriceDataFromApiAsync();
                    if (freshData != null)
                    {
                        // Cache the raw data and update dates
                        CacheRawData(freshData, todayLocal, tomorrowLocal);
                        return freshData;
                    }
                }

                // Return cached data if available
                if (!string.IsNullOrEmpty(cachedRawData))
                {
                    try
                    {
                        return JObject.Parse(cachedRawData);
                    }
                    catch
                    {
                        // If cached data is corrupted, fetch fresh
                        return await FetchRawPriceDataFromApiAsync();
                    }
                }

                // No cached data available, fetch from API
                return await FetchRawPriceDataFromApiAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRawPriceDataAsync: {ex.Message}");

                // Try to return cached data as fallback
                var cachedRawData = Preferences.Get(CACHE_KEY_RAW_DATA, "");
                if (!string.IsNullOrEmpty(cachedRawData))
                {
                    try
                    {
                        return JObject.Parse(cachedRawData);
                    }
                    catch
                    {
                        return null;
                    }
                }

                return null;
            }
        }

        private static void CacheRawData(JObject jsonData, DateTime todayDate, DateTime tomorrowDate)
        {
            try
            {
                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var prices = jsonData["prices"];
                var jsonArray = JArray.Parse(prices.ToString());
                var pricelist = JsonConvert.DeserializeObject<List<Price>>(jsonArray.ToString());

                // Check if we have data for today and tomorrow
                bool hasTodayData = pricelist.Any(x =>
                    TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Date == todayDate);

                bool hasTomorrowData = pricelist.Any(x =>
                    TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Date == tomorrowDate);

                // Store the raw JSON data
                Preferences.Set(CACHE_KEY_RAW_DATA, jsonData.ToString());
                Preferences.Set(CACHE_KEY_LAST_FETCH, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                // Store which dates we have cached
                if (hasTodayData)
                {
                    Preferences.Set(CACHE_KEY_TODAY_DATE, todayDate.ToString("yyyy-MM-dd"));
                }

                if (hasTomorrowData)
                {
                    Preferences.Set(CACHE_KEY_TOMORROW_DATE, tomorrowDate.ToString("yyyy-MM-dd"));
                }

                Console.WriteLine($"Cached price data - Today: {hasTodayData}, Tomorrow: {hasTomorrowData}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error caching data: {ex.Message}");
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
                Preferences.Remove(CACHE_KEY_TODAY_DATE);
                Preferences.Remove(CACHE_KEY_TOMORROW_DATE);
                Preferences.Remove(CACHE_KEY_RAW_DATA);
                Preferences.Remove(CACHE_KEY_LAST_FETCH);
                Console.WriteLine("Price cache cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }
    }

    public class Price
    {
        public DateTime date { get; set; }
        public double value { get; set; }
    }
}
