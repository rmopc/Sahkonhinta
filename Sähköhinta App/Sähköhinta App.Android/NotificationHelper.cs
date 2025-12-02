using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using AndroidX.Core.App;
using System;
using System.Linq;
using Resource = global::Sähköhinta_App.Droid.Resource;

namespace Sahkonhinta_App.Droid
{
    public static class NotificationHelper
    {
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
                    GetPendingIntentFlags());
            }

            var builder = new NotificationCompat.Builder(context, ChannelId)
                .SetSmallIcon(Resource.Drawable.ic_stat_electric)
                .SetContentTitle("SähköSpot")
                .SetContentText(contentText)
                .SetStyle(new NotificationCompat.BigTextStyle().BigText(bigText))
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .SetAutoCancel(false)
                .SetCategory(NotificationCompat.CategoryStatus)
                .SetPriority(NotificationCompat.PriorityLow)
                .SetVisibility((int)NotificationVisibility.Public);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                builder.SetColor(Color.ParseColor("#3498DB"));
            }

            if (pendingIntent != null)
            {
                builder.SetContentIntent(pendingIntent);
            }

            notificationManager.Notify(NotificationId, builder.Build());
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

        private static PendingIntentFlags GetPendingIntentFlags()
        {
            return Build.VERSION.SdkInt >= BuildVersionCodes.M
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                : PendingIntentFlags.UpdateCurrent;
        }
    }
}
