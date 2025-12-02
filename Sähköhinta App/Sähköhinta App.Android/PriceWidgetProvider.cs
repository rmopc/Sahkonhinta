using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Widget;
using Newtonsoft.Json;
using Sahkonhinta_App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resource = global::Sähköhinta_App.Droid.Resource;

namespace Sahkonhinta_App.Droid
{
    public class PriceWidgetProvider : AppWidgetProvider
    {
        private const string PrefsName = "SahkoHintaWidgetPrefs";
        private const string PrefDataKey = "widget_data";
        private const string PrefUpdatedKey = "widget_updated";
        private const string ClickAction = "com.sahkonhinta.widget.CLICK";
        private const string RefreshAction = "com.sahkonhinta.widget.REFRESH";

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            if (context == null || appWidgetManager == null || appWidgetIds == null)
                return;

            foreach (var widgetId in appWidgetIds)
            {
                ShowLoadingState(context, appWidgetManager, widgetId);
            }

            _ = RefreshAllWidgetsAsync(context, forceRefresh: true);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);

            if (context == null || intent?.Action == null)
                return;

            if (intent.Action == ClickAction)
            {
                var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
                if (launchIntent != null)
                {
                    launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
                    context.StartActivity(launchIntent);
                }
                return;
            }

            if (intent.Action == AppWidgetManager.ActionAppwidgetUpdate || intent.Action == RefreshAction)
            {
                var manager = AppWidgetManager.GetInstance(context);
                var widgetIds = intent.GetIntArrayExtra(AppWidgetManager.ExtraAppwidgetIds);

                if (widgetIds != null && manager != null)
                {
                    foreach (var widgetId in widgetIds)
                    {
                        ShowLoadingState(context, manager, widgetId);
                    }
                }

                _ = RefreshAllWidgetsAsync(context, forceRefresh: true);
            }
        }

        private static void ShowLoadingState(Context context, AppWidgetManager manager, int widgetId)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntent(context, views);
                views.SetTextViewText(Resource.Id.current_price_value, "Päivitetään...");
                views.SetTextViewText(Resource.Id.current_time, "");
                views.SetTextViewText(Resource.Id.update_time, "Odota hetki");
                DrawEmptyChart(views);
                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget loading error: {ex.Message}");
            }
        }

        private static void SetupClickIntent(Context context, RemoteViews views)
        {
            var intent = new Intent(context, typeof(PriceWidgetProvider));
            intent.SetAction(ClickAction);
            var flags = Build.VERSION.SdkInt >= BuildVersionCodes.M
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                : PendingIntentFlags.UpdateCurrent;

            var pendingIntent = PendingIntent.GetBroadcast(context, 0, intent, flags);
            views.SetOnClickPendingIntent(Resource.Id.widget_container, pendingIntent);
        }

        public static async Task RefreshAllWidgetsAsync(Context context, bool forceRefresh = false)
        {
            try
            {
                var jsonObject = await PriceDataService.GetRawPriceDataAsync(forceRefresh);

                var widgetData = jsonObject != null
                    ? BuildWidgetDataFromJson(jsonObject)
                    : LoadCachedWidgetData(context);

                if (jsonObject != null && widgetData != null)
                {
                    CacheWidgetData(context, widgetData);
                }

                NotificationHelper.ShowOrUpdatePriceNotification(context, widgetData);

                var manager = AppWidgetManager.GetInstance(context);
                if (manager == null)
                    return;

                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(PriceWidgetProvider)));
                var widgetIds = manager.GetAppWidgetIds(component) ?? Array.Empty<int>();

                if (widgetIds.Length == 0)
                    return;

                if (widgetData == null)
                {
                    foreach (var widgetId in widgetIds)
                    {
                        ApplyErrorState(context, manager, widgetId);
                    }
                    return;
                }

                foreach (var widgetId in widgetIds)
                {
                    ApplyWidgetData(context, manager, widgetId, widgetData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget refresh error: {ex.Message}");
            }
        }

        private static WidgetData BuildWidgetDataFromJson(Newtonsoft.Json.Linq.JObject jsonObject)
        {
            try
            {
                if (jsonObject == null || jsonObject["prices"] == null)
                    return null;

                var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
                var prices = jsonObject["prices"];
                var jsonArray = Newtonsoft.Json.Linq.JArray.Parse(prices.ToString());
                var pricelist = JsonConvert.DeserializeObject<List<Sahkonhinta_App.Services.Price>>(jsonArray.ToString());

                var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone).Date;

                // Get tax percentage and spot provision from preferences
                var taxPercentage = Xamarin.Essentials.Preferences.Get("TaxPercentage", 1.0);
                var spotProvision = Xamarin.Essentials.Preferences.Get("SpotProvision", 0.0);

                // Process today's prices
                var todayPrices = pricelist
                    .Where(x => TimeZoneInfo.ConvertTimeFromUtc(x.date, localTimeZone).Date == today)
                    .Select(p => new HourlyPrice
                    {
                        Hour = TimeZoneInfo.ConvertTimeFromUtc(p.date, localTimeZone).Hour,
                        Price = p.value / 10 * taxPercentage + spotProvision,
                        DateTime = TimeZoneInfo.ConvertTimeFromUtc(p.date, localTimeZone)
                    })
                    .OrderBy(p => p.Hour)
                    .ToList();

                if (!todayPrices.Any())
                    return null;

                // Find current hour price
                var currentHour = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone).Hour;
                var currentPrice = todayPrices.FirstOrDefault(p => p.Hour == currentHour);

                return new WidgetData
                {
                    CurrentPrice = currentPrice?.Price ?? 0,
                    MinPrice = todayPrices.Min(p => p.Price),
                    MaxPrice = todayPrices.Max(p => p.Price),
                    HourlyPrices = todayPrices,
                    LastUpdated = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building widget data: {ex.Message}");
                return null;
            }
        }

        private static void ApplyWidgetData(Context context, AppWidgetManager manager, int widgetId, WidgetData data)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntent(context, views);

                views.SetTextViewText(Resource.Id.current_price_value, $"{data.CurrentPrice:F2} c/kWh");
                views.SetTextViewText(Resource.Id.current_time, $"Hinta nyt (klo {DateTime.Now:HH})");
                views.SetTextViewText(Resource.Id.update_time, $"Päivitetty {data.LastUpdated:HH:mm}");

                var color = GetPriceColor(data.CurrentPrice, data.MinPrice, data.MaxPrice);
                views.SetTextColor(Resource.Id.current_price_value, color);

                DrawChart(context, views, data.HourlyPrices);

                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget update error: {ex.Message}");
            }
        }

        private static void ApplyErrorState(Context context, AppWidgetManager manager, int widgetId)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntent(context, views);
                views.SetTextViewText(Resource.Id.current_price_value, "Tietoja ei saatavilla");
                views.SetTextColor(Resource.Id.current_price_value, Color.ParseColor("#E74C3C"));
                views.SetTextViewText(Resource.Id.current_time, "");
                views.SetTextViewText(Resource.Id.update_time, $"Päivitetty {DateTime.Now:HH:mm}");
                DrawEmptyChart(views);
                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget error state update error: {ex.Message}");
            }
        }

        private static void CacheWidgetData(Context context, WidgetData data)
        {
            try
            {
                var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                var editor = prefs.Edit();
                editor.PutString(PrefDataKey, JsonConvert.SerializeObject(data));
                editor.PutLong(PrefUpdatedKey, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                editor.Apply();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget cache error: {ex.Message}");
            }
        }

        private static WidgetData LoadCachedWidgetData(Context context)
        {
            try
            {
                var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
                var json = prefs.GetString(PrefDataKey, null);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                var data = JsonConvert.DeserializeObject<WidgetData>(json);
                if (data != null && data.HourlyPrices == null)
                {
                    data.HourlyPrices = new List<HourlyPrice>();
                }
                return data;
            }
            catch
            {
                return null;
            }
        }

        private static void DrawChart(Context context, RemoteViews views, List<HourlyPrice> prices)
        {
            try
            {
                if (prices == null || prices.Count == 0)
                {
                    DrawEmptyChart(views);
                    return;
                }

                const int chartWidth = 280;
                const int chartHeight = 100;
                var barWidth = chartWidth / 24f;

                var bitmap = Bitmap.CreateBitmap(chartWidth, chartHeight, Bitmap.Config.Argb8888);
                var canvas = new Canvas(bitmap);
                var paint = new Paint { AntiAlias = true };

                canvas.DrawColor(Color.Transparent);

                var maxPrice = prices.Max(p => p.Price);
                var minPrice = prices.Min(p => p.Price);
                var range = maxPrice - minPrice;
                if (Math.Abs(range) < 0.0001)
                {
                    range = 1;
                }

                for (int i = 0; i < Math.Min(prices.Count, 24); i++)
                {
                    var price = prices[i];
                    var normalized = (float)((price.Price - minPrice) / range);
                    var barHeight = normalized * (chartHeight - 10);
                    var barLeft = i * barWidth + 1;
                    var barTop = chartHeight - barHeight - 5;
                    var barRight = barLeft + barWidth - 2;
                    var barBottom = chartHeight - 5;

                    paint.Color = GetPriceColor(price.Price, minPrice, maxPrice);
                    canvas.DrawRect(barLeft, barTop, barRight, barBottom, paint);

                    if (price.Hour == DateTime.Now.Hour)
                    {
                        paint.Color = Color.White;
                        paint.StrokeWidth = 2;
                        paint.SetStyle(Paint.Style.Stroke);
                        canvas.DrawRect(barLeft - 1, barTop - 1, barRight + 1, barBottom + 1, paint);
                        paint.SetStyle(Paint.Style.Fill);
                    }
                }

                views.SetImageViewBitmap(Resource.Id.price_chart, bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error drawing chart: {ex.Message}");
                DrawEmptyChart(views);
            }
        }

        private static void DrawEmptyChart(RemoteViews views)
        {
            var emptyBitmap = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Argb8888);
            views.SetImageViewBitmap(Resource.Id.price_chart, emptyBitmap);
        }

        private static Color GetPriceColor(double price, double min, double max)
        {
            var range = max - min;
            if (Math.Abs(range) < 0.0001)
            {
                return Color.ParseColor("#3498DB");
            }

            var position = (price - min) / range;

            if (position < 0.33)
                return Color.ParseColor("#27AE60");
            if (position < 0.66)
                return Color.ParseColor("#F39C12");
            return Color.ParseColor("#E74C3C");
        }

        public override void OnDeleted(Context context, int[] appWidgetIds)
        {
            base.OnDeleted(context, appWidgetIds);

            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();
            editor.Remove(PrefDataKey);
            editor.Remove(PrefUpdatedKey);
            editor.Apply();
        }
    }

    public class WidgetData
    {
        public double CurrentPrice { get; set; }
        public double MinPrice { get; set; }
        public double MaxPrice { get; set; }
        public List<HourlyPrice> HourlyPrices { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class HourlyPrice
    {
        public int Hour { get; set; }
        public double Price { get; set; }
        public DateTime DateTime { get; set; }
    }
}
