using Microsoft.Extensions.Hosting;
using NzbWebDAV.Config;
using NzbWebDAV.Tasks;
using NzbWebDAV.Utils;
using NzbWebDAV.Websocket;
using Serilog;

namespace NzbWebDAV.Services;

/// <summary>
/// Runs the RemoveUnlinkedFilesTask daily at the configured time when scheduling is enabled.
/// </summary>
public class RemoveOrphanedFilesSchedulerService : BackgroundService
{
    private readonly ConfigManager _configManager;
    private readonly WebsocketManager _websocketManager;
    private CancellationTokenSource _rescheduleCts = new();

    public RemoveOrphanedFilesSchedulerService(ConfigManager configManager, WebsocketManager websocketManager)
    {
        _configManager = configManager;
        _websocketManager = websocketManager;

        _configManager.OnConfigChanged += (_, args) =>
        {
            if (!args.ChangedConfig.ContainsKey("maintenance.remove-orphaned-schedule-enabled") &&
                !args.ChangedConfig.ContainsKey("maintenance.remove-orphaned-schedule-time"))
                return;

            var old = Interlocked.Exchange(ref _rescheduleCts, new CancellationTokenSource());
            old.Cancel();
            // Deliberately NOT disposed. ExecuteAsync may be between its field read and its
            // `.Token` access, and CancellationTokenSource.Token throws ObjectDisposedException
            // once the source is disposed. Cancelling is what wakes the loop; the cancelled
            // source is then unreferenced and collected. The linked sources ExecuteAsync builds
            // from it are still disposed by their `using`, so no registration leaks.
        };
    }

    // Sleep in slices rather than one long delay, then re-check the wall clock. A single
    // `Task.Delay(nextRun - now)` is computed against DateTime.Now, so a DST shift or any clock
    // adjustment during the wait makes it fire an hour early or late (and can double-run or skip).
    private static readonly TimeSpan MaxSleepSlice = TimeSpan.FromMinutes(30);
    private DateTime? _lastLoggedNextRun;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Read the source ONCE per iteration. `_rescheduleCts.Token` is a torn read
                // (load field, then call the property); the config handler can swap the field
                // in between, and we would take `.Token` from an object we never observed.
                var reschedule = Volatile.Read(ref _rescheduleCts);

                if (!_configManager.IsRemoveOrphanedFilesScheduleEnabled())
                {
                    using var disabledLinked = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken, reschedule.Token);
                    await Task.Delay(Timeout.Infinite, disabledLinked.Token).ConfigureAwait(false);
                    continue;
                }

                var scheduleTime = _configManager.RemoveOrphanedFilesSchedule();
                var now = DateTime.Now;
                var todayRun = now.Date + scheduleTime;
                var nextRun = todayRun > now ? todayRun : todayRun.AddDays(1);
                var delay = nextRun - now;

                // Only log when the target actually changes; we now wake every slice.
                if (_lastLoggedNextRun != nextRun)
                {
                    Log.Information("RemoveOrphanedFilesScheduler: next run scheduled at {NextRun}", nextRun);
                    _lastLoggedNextRun = nextRun;
                }

                using var delayLinked = CancellationTokenSource
                    .CreateLinkedTokenSource(stoppingToken, reschedule.Token);
                await Task.Delay(delay < MaxSleepSlice ? delay : MaxSleepSlice, delayLinked.Token)
                    .ConfigureAwait(false);

                // Woke from a slice, not from the scheduled moment. Re-evaluate against the
                // current wall clock so a DST shift cannot fire us early, twice, or not at all.
                if (DateTime.Now < nextRun) continue;

                Log.Information("RemoveOrphanedFilesScheduler: running scheduled Remove Orphaned Files task");
                var task = new RemoveUnlinkedFilesTask(_configManager, _websocketManager, isDryRun: false);
                await task.Execute().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (SigtermUtil.IsSigtermTriggered())
            {
                // OperationCanceledException is expected on sigterm
                return;
            }
            catch (OperationCanceledException)
            {
                // Config changed — loop and recompute the next run time
            }
            catch (Exception e)
            {
                Log.Error(e, "RemoveOrphanedFilesScheduler: error running scheduled task: {Message}", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ConfigureAwait(false);
            }
        }
    }
}
