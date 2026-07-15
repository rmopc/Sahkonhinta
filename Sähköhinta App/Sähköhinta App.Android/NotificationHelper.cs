using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using System;
using System.Linq;
using Resource = global::Sähköhinta_App.Droid.Resource;

namespace Sahkonhinta_App.Droid
{
    public static class NotificationHelper
    {
        private const string Tag = "SahkoWidget";
        private const string ChannelId = "sahko_price_channel";
        private const int NotificationId = 0xA11CE; // Arbitrary, consistent value

        public static void ShowOrUpdatePriceNotification(Context context, WidgetData data)
        {
            if (context == null)
                return;

            var notificationManager = (NotificationManager)context.GetSystemService(Context.NotificationService);
            if (notificationManager == null)
                return;

            EnsureChannel(notificationManager);

            string contentText;
            string bigText;

            if (data != null)
            {
                var avgPrice = data.HourlyPrices != null && data.HourlyPrices.Count > 0
                    ? data.HourlyPrices.Average(p => p.Price)
                    : data.CurrentPrice;

                contentText = $"Nyt {data.CurrentPrice:F2} c/kWh";
                bigText = string.Format(
                    "Nyt {0:F2} c/kWh\nAlin {1:F2} • Ylin {2:F2} • Keskihinta {3:F2}",
                    data.CurrentPrice,
                    data.MinPrice,
                    data.MaxPrice,
                    avgPrice);
            }
            else
            {
                contentText = "Hintatietoja ei saatavilla";
                bigText = "Hintatietojen päivitys epäonnistui. Yritetään uudelleen pian.";
            }

            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName);
            PendingIntent pendingIntent = null;

            if (launchIntent != null)
            {
                launchIntent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.NewTask);
                pendingIntent = PendingIntent.GetActivity(
                    context,
                    0,
                    launchIntent,
                    PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            }

            var builder = new Notification.Builder(context, ChannelId)
                .SetSmallIcon(CreateStatusBarIcon(context, data))
                .SetContentTitle("SähköSpot")
                .SetContentText(contentText)
                .SetStyle(new Notification.BigTextStyle().BigText(bigText))
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .SetAutoCancel(false)
                .SetCategory(Notification.CategoryStatus)
                .SetVisibility(NotificationVisibility.Public)
                .SetColor(Color.ParseColor("#3498DB").ToArgb());

            if (pendingIntent != null)
            {
                builder.SetContentIntent(pendingIntent);
            }

            notificationManager.Notify(NotificationId, builder.Build());
        }

        /// <summary>
        /// Renders the current price as text into the notification's small icon so
        /// the number itself shows in the status bar (next to clock/battery/wifi).
        /// Status bar icons are alpha-masked and tinted by the system, so the text
        /// is drawn in plain white and ends up white/grey like the system icons.
        /// Falls back to the static lightning bolt icon when there is no data.
        /// </summary>
        private static Icon CreateStatusBarIcon(Context context, WidgetData data)
        {
            if (data == null)
                return Icon.CreateWithResource(context, Resource.Drawable.ic_stat_electric);

            try
            {
                var text = data.CurrentPrice.ToString("F2");

                // The status bar scales the whole bitmap into a square icon slot,
                // so the string's width is what limits digit size. Everything here
                // fights for width: tight crop (no padding), condensed bold face
                // and negative letter spacing.
                var pricePaint = new Paint
                {
                    AntiAlias = true,
                    Color = Color.White,
                    TextAlign = Paint.Align.Left,
                    LetterSpacing = -0.06f
                };
                pricePaint.SetTypeface(Typeface.Create("sans-serif-condensed", TypefaceStyle.Bold));
                pricePaint.TextSize = 128f;

                var priceBounds = new Rect();
                pricePaint.GetTextBounds(text, 0, text.Length, priceBounds);

                const int margin = 2;
                var width = Math.Max(1, priceBounds.Width() + 2 * margin);
                var height = Math.Max(1, priceBounds.Height() + 2 * margin);

                var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888);
                var canvas = new Canvas(bitmap);

                // Offset so the measured glyph box starts at the margin.
                canvas.DrawText(text, margin - priceBounds.Left, margin - priceBounds.Top, pricePaint);

                return Icon.CreateWithBitmap(bitmap);
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Status bar icon error: {ex.Message}");
                return Icon.CreateWithResource(context, Resource.Drawable.ic_stat_electric);
            }
        }

        public static void CancelPriceNotification(Context context)
        {
            var notificationManager = (NotificationManager)context?.GetSystemService(Context.NotificationService);
            notificationManager?.Cancel(NotificationId);
        }

        private static void EnsureChannel(NotificationManager manager)
        {
            if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                return;

            var channel = manager.GetNotificationChannel(ChannelId);
            if (channel != null)
                return;

            channel = new NotificationChannel(ChannelId, "Sähkön hinta", NotificationImportance.Low)
            {
                Description = "Näyttää ajantasaisen sähkön spot-hinnan",
                LockscreenVisibility = NotificationVisibility.Public
            };
            channel.EnableVibration(false);
            channel.EnableLights(false);
            manager.CreateNotificationChannel(channel);
        }
    }
}
