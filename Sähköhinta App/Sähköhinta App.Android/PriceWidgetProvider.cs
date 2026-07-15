using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
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
    [BroadcastReceiver(Label = "SähköSpot", Exported = true, Name = "com.sahkonhinta.widget.PriceWidgetProvider")]
    [IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_info")]
    public class PriceWidgetProvider : AppWidgetProvider
    {
        private const string Tag = "SahkoWidget";
        private const string PrefsName = "SahkoHintaWidgetPrefs";
        private const string PrefDataKey = "widget_data";
        private const string PrefUpdatedKey = "widget_updated";
        private const string RefreshAction = "com.sahkonhinta.widget.REFRESH";

        // Cache older than this forces a network fetch even if it covers the current hour.
        private static readonly TimeSpan MaxCacheAge = TimeSpan.FromHours(6);

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            if (context == null || appWidgetManager == null || appWidgetIds == null)
                return;

            // Never leave the widget empty: render whatever we have cached right away,
            // then let WorkManager fetch fresh data (a broadcast receiver must not do
            // network work itself - the process can be killed as soon as OnReceive returns).
            RenderCachedData(context, appWidgetManager, appWidgetIds);

            WidgetUpdateScheduler.ScheduleWidgetUpdates(context);
            WidgetUpdateScheduler.TriggerImmediateUpdate(context);
        }

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);

            if (context == null || intent?.Action == null)
                return;

            if (intent.Action == RefreshAction)
            {
                ShowRefreshingHint(context);
                WidgetUpdateScheduler.TriggerImmediateUpdate(context, forceRefresh: true);
            }
        }

        public override void OnAppWidgetOptionsChanged(Context context, AppWidgetManager appWidgetManager, int appWidgetId, Bundle newAppWidgetOptions)
        {
            base.OnAppWidgetOptionsChanged(context, appWidgetManager, appWidgetId, newAppWidgetOptions);

            if (context == null || appWidgetManager == null)
                return;

            // Re-render the chart bitmap at the widget's new size.
            RenderCachedData(context, appWidgetManager, new[] { appWidgetId });
        }

        private static void ShowRefreshingHint(Context context)
        {
            try
            {
                var manager = AppWidgetManager.GetInstance(context);
                var widgetIds = GetWidgetIds(context, manager);

                foreach (var widgetId in widgetIds)
                {
                    var partialViews = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                    partialViews.SetTextViewText(Resource.Id.update_time, "Päivitetään...");
                    manager.PartiallyUpdateAppWidget(widgetId, partialViews);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"Refresh hint error: {ex.Message}");
            }
        }

        private static void RenderCachedData(Context context, AppWidgetManager manager, int[] widgetIds)
        {
            try
            {
                var data = LoadCachedWidgetData(context);
                var nowLocal = GetHelsinkiNow();
                var hasTodayData = data?.HourlyPrices != null
                    && data.HourlyPrices.Any(p => p.DateTime.Date == nowLocal.Date);

                if (hasTodayData)
                {
                    UpdateCurrentPrice(data);
                    foreach (var widgetId in widgetIds)
                    {
                        ApplyWidgetData(context, manager, widgetId, data);
                    }
                }
                else
                {
                    // Only on first add (or after a day change with no fetch yet)
                    // do we show a loading state - fresh data is already on its way.
                    foreach (var widgetId in widgetIds)
                    {
                        ShowLoadingState(context, manager, widgetId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Cached render error: {ex.Message}");
            }
        }

        private static void ShowLoadingState(Context context, AppWidgetManager manager, int widgetId)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntents(context, views);
                views.SetTextViewText(Resource.Id.current_price_value, "Päivitetään...");
                views.SetTextViewText(Resource.Id.current_time, "");
                views.SetTextViewText(Resource.Id.update_time, "Odota hetki");
                DrawEmptyChart(views);
                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Widget loading state error: {ex.Message}");
            }
        }

        private static void SetupClickIntents(Context context, RemoteViews views)
        {
            var flags = Build.VERSION.SdkInt >= BuildVersionCodes.M
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                : PendingIntentFlags.UpdateCurrent;

            // Tapping the widget opens the app directly.
            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
            if (launchIntent != null)
            {
                launchIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
                var launchPending = PendingIntent.GetActivity(context, 0, launchIntent, flags);
                views.SetOnClickPendingIntent(Resource.Id.widget_container, launchPending);
            }

            // The refresh button broadcasts back to this provider.
            var refreshIntent = new Intent(context, typeof(PriceWidgetProvider));
            refreshIntent.SetAction(RefreshAction);
            var refreshPending = PendingIntent.GetBroadcast(context, 1, refreshIntent, flags);
            views.SetOnClickPendingIntent(Resource.Id.widget_refresh, refreshPending);
        }

        /// <summary>
        /// Fetches price data (or reuses the cache when it still covers the current hour
        /// and forceRefresh is false), then updates the notification and all widgets.
        /// Runs inside WidgetUpdateWorker so the process stays alive for the duration.
        /// Returns false when no data could be shown so the worker can retry.
        /// </summary>
        public static async Task<bool> RefreshAllWidgetsAsync(Context context, bool forceRefresh = false)
        {
            try
            {
                WidgetData widgetData = null;

                if (!forceRefresh)
                {
                    var cached = LoadCachedWidgetData(context);
                    if (IsCacheCurrent(cached))
                    {
                        widgetData = cached;
                    }
                }

                if (widgetData == null)
                {
                    var jsonObject = await PriceDataService.GetRawPriceDataAsync(forceRefresh: true);
                    widgetData = jsonObject != null ? BuildWidgetDataFromJson(jsonObject) : null;

                    if (widgetData != null)
                    {
                        CacheWidgetData(context, widgetData);
                    }
                    else
                    {
                        // Fetch failed - fall back to whatever we have so the
                        // widget and notification keep showing real data.
                        widgetData = LoadCachedWidgetData(context);
                        Log.Warn(Tag, "Fetch failed, falling back to cached data");
                    }
                }

                if (widgetData != null)
                {
                    UpdateCurrentPrice(widgetData);
                }

                NotificationHelper.ShowOrUpdatePriceNotification(context, widgetData);

                var manager = AppWidgetManager.GetInstance(context);
                if (manager == null)
                    return widgetData != null;

                var widgetIds = GetWidgetIds(context, manager);

                foreach (var widgetId in widgetIds)
                {
                    if (widgetData != null)
                    {
                        ApplyWidgetData(context, manager, widgetId, widgetData);
                    }
                    else
                    {
                        ApplyErrorState(context, manager, widgetId);
                    }
                }

                return widgetData != null;
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Widget refresh error: {ex}");
                return false;
            }
        }

        private static int[] GetWidgetIds(Context context, AppWidgetManager manager)
        {
            if (manager == null)
                return Array.Empty<int>();

            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(PriceWidgetProvider)));
            return manager.GetAppWidgetIds(component) ?? Array.Empty<int>();
        }

        private static DateTime GetHelsinkiNow()
        {
            var localTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, localTimeZone);
        }

        /// <summary>
        /// The cache is current when it was fetched recently and contains a price
        /// for the current Helsinki hour. Spot prices are day-ahead and never change
        /// retroactively, so a same-day cache is as good as a fresh fetch.
        /// </summary>
        private static bool IsCacheCurrent(WidgetData data)
        {
            if (data?.HourlyPrices == null || data.HourlyPrices.Count == 0)
                return false;

            if (DateTime.Now - data.LastUpdated > MaxCacheAge)
                return false;

            var nowLocal = GetHelsinkiNow();
            return data.HourlyPrices.Any(p => p.DateTime.Date == nowLocal.Date && p.Hour == nowLocal.Hour);
        }

        /// <summary>
        /// Recomputes CurrentPrice from the stored hourly prices so that cached
        /// data rendered at a later hour still highlights the right price.
        /// </summary>
        private static void UpdateCurrentPrice(WidgetData data)
        {
            if (data?.HourlyPrices == null)
                return;

            var nowLocal = GetHelsinkiNow();
            var current = data.HourlyPrices.FirstOrDefault(p => p.DateTime.Date == nowLocal.Date && p.Hour == nowLocal.Hour);
            if (current != null)
            {
                data.CurrentPrice = current.Price;
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
                Log.Error(Tag, $"Error building widget data: {ex.Message}");
                return null;
            }
        }

        private static void ApplyWidgetData(Context context, AppWidgetManager manager, int widgetId, WidgetData data)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntents(context, views);

                var nowLocal = GetHelsinkiNow();
                views.SetTextViewText(Resource.Id.current_price_value, $"{data.CurrentPrice:F2} c/kWh");
                views.SetTextViewText(Resource.Id.current_time, $"Hinta nyt (klo {nowLocal:HH})");
                views.SetTextViewText(Resource.Id.update_time, $"Päivitetty {data.LastUpdated:HH:mm}");

                var (lowBound, midBound) = GetTercileBounds(data.HourlyPrices?.Select(p => p.Price).ToList());
                var color = GetPriceColor(data.CurrentPrice, lowBound, midBound);
                views.SetTextColor(Resource.Id.current_price_value, color);

                DrawChart(context, manager, views, widgetId, data.HourlyPrices, nowLocal.Hour);

                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Widget update error: {ex.Message}");
            }
        }

        private static void ApplyErrorState(Context context, AppWidgetManager manager, int widgetId)
        {
            try
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_layout);
                SetupClickIntents(context, views);
                views.SetTextViewText(Resource.Id.current_price_value, "Tietoja ei saatavilla");
                views.SetTextColor(Resource.Id.current_price_value, Color.ParseColor("#FB7185"));
                views.SetTextViewText(Resource.Id.current_time, "");
                views.SetTextViewText(Resource.Id.update_time, $"Päivitetty {DateTime.Now:HH:mm}");
                DrawEmptyChart(views);
                manager.UpdateAppWidget(widgetId, views);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Widget error state update error: {ex.Message}");
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
                Log.Error(Tag, $"Widget cache error: {ex.Message}");
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

        /// <summary>
        /// Determines the chart bitmap size in pixels from the widget's actual
        /// size (dp) and the screen density, so the chart stays sharp on any screen.
        /// </summary>
        private static (int width, int height) GetChartSizePx(Context context, AppWidgetManager manager, int widgetId)
        {
            var density = context.Resources?.DisplayMetrics?.Density ?? 2f;
            var widthDp = 250;
            var heightDp = 90;

            try
            {
                var options = manager.GetAppWidgetOptions(widgetId);
                if (options != null)
                {
                    var minWidth = options.GetInt(AppWidgetManager.OptionAppwidgetMinWidth);
                    var maxHeight = options.GetInt(AppWidgetManager.OptionAppwidgetMaxHeight);

                    // Subtract the container/chart padding; leave room for the
                    // header row and the current price card above the chart.
                    if (minWidth > 0) widthDp = minWidth - 24;
                    if (maxHeight > 0) heightDp = maxHeight - 115;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"Widget options unavailable: {ex.Message}");
            }

            widthDp = Math.Max(120, Math.Min(widthDp, 600));
            heightDp = Math.Max(50, Math.Min(heightDp, 250));

            return ((int)(widthDp * density), (int)(heightDp * density));
        }

        private static void DrawChart(Context context, AppWidgetManager manager, RemoteViews views, int widgetId, List<HourlyPrice> prices, int currentHour)
        {
            try
            {
                if (prices == null || prices.Count == 0)
                {
                    DrawEmptyChart(views);
                    return;
                }

                var (chartWidth, chartHeight) = GetChartSizePx(context, manager, widgetId);
                var density = context.Resources?.DisplayMetrics?.Density ?? 2f;
                var barWidth = chartWidth / 24f;
                var barGap = Math.Max(1f, density);
                var verticalPadding = 5f * density;

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

                var (lowBound, midBound) = GetTercileBounds(prices.Select(p => p.Price).ToList());

                for (int i = 0; i < Math.Min(prices.Count, 24); i++)
                {
                    var price = prices[i];
                    var normalized = (float)((price.Price - minPrice) / range);
                    // Keep a minimum bar height so cheap hours stay visible.
                    var barHeight = Math.Max(2f * density, normalized * (chartHeight - 2 * verticalPadding));
                    var barLeft = i * barWidth + barGap / 2;
                    var barTop = chartHeight - barHeight - verticalPadding;
                    var barRight = barLeft + barWidth - barGap;
                    var barBottom = chartHeight - verticalPadding;

                    paint.Color = GetPriceColor(price.Price, lowBound, midBound);
                    canvas.DrawRect(barLeft, barTop, barRight, barBottom, paint);

                    if (price.Hour == currentHour)
                    {
                        paint.Color = Color.ParseColor("#E6EBFF");
                        paint.StrokeWidth = Math.Max(2f, density);
                        paint.SetStyle(Paint.Style.Stroke);
                        canvas.DrawRect(barLeft - 1, barTop - 1, barRight + 1, barBottom + 1, paint);
                        paint.SetStyle(Paint.Style.Fill);
                    }
                }

                views.SetImageViewBitmap(Resource.Id.price_chart, bitmap);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Error drawing chart: {ex.Message}");
                DrawEmptyChart(views);
            }
        }

        private static void DrawEmptyChart(RemoteViews views)
        {
            var emptyBitmap = Bitmap.CreateBitmap(1, 1, Bitmap.Config.Argb8888);
            views.SetImageViewBitmap(Resource.Id.price_chart, emptyBitmap);
        }

        // Tercile-based categorization (Aurora design system): sort the day's
        // prices, split into thirds; each price is colored by its third.
        private static (double lowBound, double midBound) GetTercileBounds(List<double> values)
        {
            if (values == null || values.Count == 0)
            {
                return (double.NaN, double.NaN);
            }

            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            var lowBound = sorted[Math.Max(0, (int)Math.Ceiling(n / 3.0) - 1)];
            var midBound = sorted[Math.Max(0, (int)Math.Ceiling(n * 2 / 3.0) - 1)];
            return (lowBound, midBound);
        }

        private static Color GetPriceColor(double price, double lowBound, double midBound)
        {
            if (double.IsNaN(lowBound) || double.IsNaN(midBound))
            {
                return Color.ParseColor("#38BDF8"); // Sky fallback when no data
            }

            if (price <= lowBound)
                return Color.ParseColor("#34D399"); // Mint (cheapest tercile)
            if (price <= midBound)
                return Color.ParseColor("#FBBF24"); // Amber (middle tercile)
            return Color.ParseColor("#FB7185");     // Rose (most expensive tercile)
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
