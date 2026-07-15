using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Util;
using AndroidX.Work;
using Java.Util.Concurrent;
using System;

namespace Sahkonhinta_App.Droid
{
    public class WidgetUpdateWorker : Worker
    {
        public const string ForceRefreshKey = "force_refresh";
        private const string Tag = "SahkoWidget";

        public WidgetUpdateWorker(Context context, WorkerParameters workerParams) : base(context, workerParams)
        {
        }

        public override Result DoWork()
        {
            try
            {
                var forceRefresh = InputData.GetBoolean(ForceRefreshKey, false);

                // Blocking here is fine: DoWork runs on a WorkManager background
                // thread, and WorkManager keeps the process alive until we return.
                var success = PriceWidgetProvider.RefreshAllWidgetsAsync(ApplicationContext, forceRefresh)
                    .GetAwaiter()
                    .GetResult();

                if (success)
                    return Result.InvokeSuccess();

                // No data could be shown (e.g. offline with an empty cache):
                // retry with backoff a few times, then wait for the next period.
                return RunAttemptCount < 3 ? Result.InvokeRetry() : Result.InvokeFailure();
            }
            catch (Exception ex)
            {
                Log.Error(Tag, $"Widget update worker error: {ex.Message}");
                return RunAttemptCount < 3 ? Result.InvokeRetry() : Result.InvokeFailure();
            }
        }
    }

    public static class WidgetUpdateScheduler
    {
        private const string WorkTag = "sahko_widget_update";
        private const string OneTimeWorkName = "sahko_widget_update_once";

        /// <summary>
        /// Periodic refresh every 15 minutes (WorkManager's minimum). The worker
        /// reuses the day's cached prices when they still cover the current hour,
        /// so this keeps the widget, notification and status bar price current
        /// across hour changes without hitting the network each time.
        /// </summary>
        public static void ScheduleWidgetUpdates(Context context)
        {
            var periodicWorkRequest = new PeriodicWorkRequest.Builder(typeof(WidgetUpdateWorker), 15, TimeUnit.Minutes)
                .AddTag(WorkTag)
                .Build();

            WorkManager.GetInstance(context).EnqueueUniquePeriodicWork(
                WorkTag,
                ExistingPeriodicWorkPolicy.Keep,
                periodicWorkRequest);
        }

        public static void TriggerImmediateUpdate(Context context, bool forceRefresh = false)
        {
            var inputData = new Data.Builder()
                .PutBoolean(WidgetUpdateWorker.ForceRefreshKey, forceRefresh)
                .Build();

            // The binding's SetInputData/SetExpedited return the base
            // WorkRequest.Builder, so build unchained and cast the result.
            var builder = new OneTimeWorkRequest.Builder(typeof(WidgetUpdateWorker));
            builder.SetInputData(inputData);
            builder.SetExpedited(OutOfQuotaPolicy.RunAsNonExpeditedWorkRequest);
            var oneTimeRequest = (OneTimeWorkRequest)builder.Build();

            WorkManager.GetInstance(context).EnqueueUniqueWork(
                OneTimeWorkName,
                ExistingWorkPolicy.Replace,
                oneTimeRequest);
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
            }
        }
    }
}
