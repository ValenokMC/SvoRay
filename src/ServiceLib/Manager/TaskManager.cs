namespace ServiceLib.Manager;

public class TaskManager
{
    private static readonly Lazy<TaskManager> _instance = new(() => new());
    public static TaskManager Instance => _instance.Value;
    private Config _config;
    private Func<bool, string, Task>? _updateFunc;

    public void RegUpdateTask(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;

        Task.Run(ScheduledTasks);
    }

    private async Task ScheduledTasks()
    {
        Logging.SaveLog("Setup Scheduled Tasks");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        var numOfExecuted = 1;
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            //Execute once 1 minute
            try
            {
                await UpdateTaskRunSubscription();
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ScheduledTasks - UpdateTaskRunSubscription", ex);
            }

            //Execute once 20 minute
            if (numOfExecuted % 20 == 0)
            {
                //Logging.SaveLog("Execute save config");

                try
                {
                    await ConfigHandler.SaveConfig(_config);
                    await ProfileExManager.Instance.SaveTo();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - SaveConfig", ex);
                }
            }

            //Execute once 1 hour
            if (numOfExecuted % 60 == 0)
            {
                //Logging.SaveLog("Execute delete expired files");

                FileUtils.DeleteExpiredFiles(Utils.GetBinConfigPath(), DateTime.Now.AddHours(-1), "Test");
                FileUtils.DeleteExpiredFiles(Utils.GetLogPath(), DateTime.Now.AddDays(-7));
                FileUtils.DeleteExpiredFiles(Utils.GetTempPath(), DateTime.Now.AddDays(-7));

                try
                {
                    await UpdateTaskRunGeo();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - UpdateTaskRunGeo", ex);
                }
            }

            //Execute once 24 hour
            if (numOfExecuted % 1440 == 1)
            {
                try
                {
                    await UpdateTaskRunCheckUpdate();
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("ScheduledTasks - UpdateTaskRunCheckUpdate", ex);
                }
            }
            numOfExecuted++;
        }
    }

    private async Task UpdateTaskRunSubscription()
    {
        var updateTime = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
        var lstSubs = (await AppManager.Instance.SubItems())?
            .Where(t => SvoRayAutoUpdate.IsSubscriptionDue(t, updateTime))
            .ToList();

        if (lstSubs is not { Count: > 0 })
        {
            return;
        }

        Logging.SaveLog("Execute update subscription");

        foreach (var item in lstSubs)
        {
            // The handler reloads the core only when the refreshed list or rules changed its
            // config: this runs every few hours while the user is in a call or a game.
            var updated = await SubscriptionHandler.UpdateProcess(_config, item.Id, true, async (success, msg) =>
            {
                await _updateFunc?.Invoke(success, msg);
                if (success)
                {
                    Logging.SaveLog($"Update subscription end. {msg}");
                }
            });
            if (!updated)
            {
                Logging.SaveLog($"Update subscription failed, retrying in {SvoRayAutoUpdate.FailedRetryMinutes} min");
            }
            item.UpdateTime = SvoRayAutoUpdate.UpdateTimeAfterAttempt(item, updateTime, updated);
            await ConfigHandler.AddSubItem(_config, item);
            await Task.Delay(1000);
        }
    }

    private async Task UpdateTaskRunGeo()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // No reload follows (the handler is told "not a success"): a running core keeps the
        // files it loaded, and the new ones are used from the next connect.
        if (SvoRayAutoUpdate.IsGeoUpdateDue(_config.GuiItem.AutoUpdateInterval, _config.SvoRayItem.LastGeoUpdate, now))
        {
            Logging.SaveLog("Execute update geo files");

            await new UpdateService(_config, async (success, msg) =>
            {
                await _updateFunc?.Invoke(false, msg);
            }).UpdateGeoFileAll();
            // UpdateGeoFileAll reports a file that failed only as a message, and an exception
            // skips this line: a run that got this far counts, and the next is an interval away.
            _config.SvoRayItem.LastGeoUpdate = now;
            await ConfigHandler.SaveConfig(_config);
            // The simple profile names only categories the files had; rebuilt so a category that
            // is gone does not stop the core at its next start. No reload: see above.
            await SvoRayRoutingHandler.RefreshIfActiveAsync(_config);
        }
    }

    private async Task UpdateTaskRunCheckUpdate()
    {
        Logging.SaveLog("Execute check update");

        var updateService = new UpdateService(_config, async (success, msg) => await Task.CompletedTask);

        var msgs = await updateService.CheckHasUpdateOnlyAll(_config.CheckUpdateItem.UpdateViaProxy);
        foreach (var msg in msgs)
        {
            await _updateFunc?.Invoke(false, msg);
        }
        NoticeManager.Instance.Enqueue(string.Join("\n", msgs));

        if (msgs.Count > 0)
        {
            AppEvents.HasUpdateNotified.Publish(true);
        }
    }
}
