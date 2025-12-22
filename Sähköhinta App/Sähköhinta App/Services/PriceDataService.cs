using System;
using System.Collections.Generic;
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
        public List<Price> Prices { get; set; }
    }

    public class PriceDataService
    {
        private const string API_URL = "https://oljemark.net/electricity_prices.json";

        public static async Task<(DayPriceData today, DayPriceData tomorrow)> GetPriceDataAsync(bool forceRefresh = false)
        {
            try
            {
                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);
                var todayLocal = nowLocal.Date;
                var tomorrowLocal = todayLocal.AddDays(1);

                var jsonObject = await FetchPriceDataFromApiAsync();
                if (jsonObject == null)
                {
                    Console.WriteLine("Failed to fetch price data from API");
                    return (null, null);
                }

                // Parse today's prices from the "today" array
                var todayArray = jsonObject["today"] as JArray;
                var todayPrices = ParsePricesFromArray(todayArray, todayLocal);

                DayPriceData todayData = null;
                if (todayPrices.Any())
                {
                    todayData = new DayPriceData
                    {
                        Date = todayLocal,
                        Prices = todayPrices
                    };
                    Console.WriteLine($"Loaded today's data: {todayLocal:yyyy-MM-dd} ({todayPrices.Count} hours)");
                }

                // Parse tomorrow's prices from the "tomorrow" array
                var tomorrowArray = jsonObject["tomorrow"] as JArray;
                var tomorrowPrices = ParsePricesFromArray(tomorrowArray, tomorrowLocal);

                DayPriceData tomorrowData = null;
                if (tomorrowPrices.Any())
                {
                    tomorrowData = new DayPriceData
                    {
                        Date = tomorrowLocal,
                        Prices = tomorrowPrices
                    };
                    Console.WriteLine($"Loaded tomorrow's data: {tomorrowLocal:yyyy-MM-dd} ({tomorrowPrices.Count} hours)");
                }

                return (todayData, tomorrowData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPriceDataAsync: {ex.Message}");
                return (null, null);
            }
        }

        private static List<Price> ParsePricesFromArray(JArray priceArray, DateTime expectedDate)
        {
            var prices = new List<Price>();
            if (priceArray == null)
                return prices;

            var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");

            foreach (var item in priceArray)
            {
                try
                {
                    // Parse the datetime field which includes timezone offset
                    var datetimeStr = item["datetime"]?.ToString();
                    if (string.IsNullOrEmpty(datetimeStr))
                        continue;

                    var datetime = DateTimeOffset.Parse(datetimeStr);
                    var datetimeUtc = datetime.UtcDateTime;
                    var datetimeLocal = TimeZoneInfo.ConvertTimeFromUtc(datetimeUtc, localTimeZone);

                    // Only include prices that match the expected date
                    if (datetimeLocal.Date != expectedDate)
                        continue;

                    // Use price_cents_kwh which is already converted to c/kWh
                    var priceCentsKwh = (double?)item["price_cents_kwh"];
                    if (!priceCentsKwh.HasValue)
                        continue;

                    // Convert back to the internal format (EUR/MWh * 10) for compatibility
                    // price_cents_kwh is in c/kWh, so multiply by 10 to get EUR/MWh * 10
                    var priceValue = priceCentsKwh.Value * 10;

                    prices.Add(new Price
                    {
                        date = datetimeUtc,
                        value = priceValue
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error parsing price item: {ex.Message}");
                    continue;
                }
            }

            return prices.OrderBy(p => p.date).ToList();
        }

        private static async Task<JObject> FetchPriceDataFromApiAsync()
        {
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    var response = await httpClient.GetAsync(API_URL);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();

                    if (string.IsNullOrEmpty(json) || json.Length > 10000000)
                    {
                        Console.WriteLine("Invalid API response: empty or too large");
                        return null;
                    }

                    var jsonObject = JObject.Parse(json);

                    if (jsonObject?["today"] == null)
                    {
                        Console.WriteLine("Invalid API response: missing 'today' array");
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
