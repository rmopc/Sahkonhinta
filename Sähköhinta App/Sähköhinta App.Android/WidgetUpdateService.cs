using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using AndroidX.Work;
using Java.Util.Concurrent;
using System;
using System.Threading.Tasks;

namespace Sahkonhinta_App.Droid
{
    public class WidgetUpdateWorker : Worker
    {
        public WidgetUpdateWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
        {
        }

        public override Result DoWork()
        {
            try
            {
                PriceWidgetProvider.RefreshAllWidgetsAsync(ApplicationContext, forceRefresh: true)
                    .GetAwaiter()
                    .GetResult();

                return Result.InvokeSuccess();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Widget update worker error: {ex.Message}");
                return Result.InvokeRetry();
            }
        }
    }

    public static class WidgetUpdateScheduler
    {
        private const string WorkTag = "sahko_widget_update";
        private const string OneTimeTag = "sahko_widget_update_once";

        public static void ScheduleWidgetUpdates(Context context)
        {
            var constraints = new Constraints.Builder()
                .SetRequiredNetworkType(NetworkType.Connected)
                .Build();

            var periodicWorkRequest = new PeriodicWorkRequest.Builder(typeof(WidgetUpdateWorker), 1, TimeUnit.Hours)
                .SetConstraints(constraints)
                .AddTag(WorkTag)
                .Build();

            WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
                WorkTag,
                ExistingPeriodicWorkPolicy.Replace,
                periodicWorkRequest);
        }

        public static void TriggerImmediateUpdate(Context context)
        {
            var oneTimeRequest = new OneTimeWorkRequest.Builder(typeof(WidgetUpdateWorker))
                .AddTag(OneTimeTag)
                .Build();

            WorkManager.GetInstance(context).Enqueue(oneTimeRequest);
        }

        public static void CancelWidgetUpdates(Context context)
        {
            WorkManager.GetInstance(context).CancelAllWorkByTag(WorkTag);
        }
    }

    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
    public class BootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (context == null || intent?.Action == null)
                return;

            if (intent.Action == Intent.ActionBootCompleted || intent.Action == Intent.ActionMyPackageReplaced)
            {
                WidgetUpdateScheduler.ScheduleWidgetUpdates(context);
                WidgetUpdateScheduler.TriggerImmediateUpdate(context);
                Task.Run(() => PriceWidgetProvider.RefreshAllWidgetsAsync(context, forceRefresh: true));
            }
        }
    }
}
