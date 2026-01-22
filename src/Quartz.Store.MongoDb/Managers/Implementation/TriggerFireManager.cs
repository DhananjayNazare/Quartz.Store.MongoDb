using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
    using Common.Logging;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using Quartz.Store.MongoDb.Models;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages trigger firing, acquisition, and misfire recovery.</summary>
internal class TriggerFireManager : BaseStorageManager, ITriggerFireManager
{
    private readonly TriggerRepository _triggerRepository;
    private readonly JobDetailRepository _jobDetailRepository;
    private readonly FiredTriggerRepository _firedTriggerRepository;
    private readonly CalendarRepository _calendar_repository;
    private readonly ISchedulerSignaler _schedulerSignaler;
    private readonly TimeSpan _misfireThreshold;
    private readonly string _instanceId;
    private static readonly ILog Log = LogManager.GetLogger<TriggerFireManager>();

    public TriggerFireManager(
        TriggerRepository triggerRepository,
        JobDetailRepository jobDetailRepository,
        FiredTriggerRepository firedTriggerRepository,
        CalendarRepository calendarRepository,
        LockManager lockManager,
        ISchedulerSignaler schedulerSignaler,
        string instanceId,
        TimeSpan misfireThreshold)
        : base(lockManager, instanceId)
    {
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _jobDetailRepository = jobDetailRepository ?? throw new ArgumentNullException(nameof(jobDetailRepository));
        _firedTriggerRepository = firedTriggerRepository ?? throw new ArgumentNullException(nameof(firedTriggerRepository));
        _calendar_repository = calendarRepository ?? throw new ArgumentNullException(nameof(calendarRepository));
        _schedulerSignaler = schedulerSignaler ?? throw new ArgumentNullException(nameof(schedulerSignaler));
        _misfireThreshold = misfireThreshold;
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
    }

    public async Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken token)
    {
        return await ExecuteWithLock(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var windowUpper = noLaterThan + timeWindow;
            var windowLower = now - _misfireThreshold;

            var keys = await _triggerRepository.GetTriggersToAcquire(windowUpper, windowLower, maxCount).ConfigureAwait(false);
            var acquired = new List<IOperableTrigger>();

            foreach (var key in keys)
            {
                token.ThrowIfCancellationRequested();
                var updated = await _triggerRepository.UpdateTriggerState(key, Models.TriggerState.Acquired, Models.TriggerState.Waiting).ConfigureAwait(false);
                if (updated > 0)
                {
                    var model = await _triggerRepository.GetTrigger(key).ConfigureAwait(false);
                    if (model?.GetTrigger() is IOperableTrigger op)
                    {
                        acquired.Add(op);
                    }
                }
            }

            return acquired;
        }, token);
    }

    public async Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken token)
    {
        await ExecuteWithLock(async () =>
        {
            await _triggerRepository.UpdateTriggerState(trigger.Key, Models.TriggerState.Waiting, Models.TriggerState.Acquired).ConfigureAwait(false);
        }, token);
    }

    public async Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken token)
    {
        return await ExecuteWithLock(async () =>
        {
            var results = new List<TriggerFiredResult>();

            foreach (var trigger in triggers)
            {
                try
                {
                    var bundle = await TriggerFiredInternal(trigger).ConfigureAwait(false);
                    results.Add(new TriggerFiredResult(bundle));
                }
                catch (Exception ex)
                {
                    results.Add(new TriggerFiredResult(ex));
                }
            }

            return results;
        }, token);
    }

    public async Task TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken token)
    {
        await ExecuteWithLock(async () =>
        {
            await TriggeredJobCompleteInternal(trigger, jobDetail, triggerInstCode, token).ConfigureAwait(false);
        }, token);
    }

    public async Task<RecoverMisfiredJobsResult> DoRecoverMisfires()
    {
        // This will be called by MisfireHandler
        try
        {
            var misfireTime = DateTimeOffset.UtcNow - _misfireThreshold;
            var misfireCount = await _triggerRepository.GetMisfireCount(misfireTime.UtcDateTime).ConfigureAwait(false);
            
            if (misfireCount == 0)
            {
                Log.Debug("Found 0 triggers that missed their scheduled fire-time.");
                return RecoverMisfiredJobsResult.NoOp;
            }

            return await ExecuteWithLock(async () =>
            {
                return await RecoverMisfiredJobsInternal(false, 20).ConfigureAwait(false);
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    public async Task RecoverMisfiredJobs(bool recovering, CancellationToken token)
    {
        await ExecuteWithLock(async () =>
        {
            await RecoverMisfiredJobsInternal(recovering, recovering ? -1 : 20).ConfigureAwait(false);
        }, token);
    }

    private async Task<RecoverMisfiredJobsResult> RecoverMisfiredJobsInternal(bool recovering, int maxMisfiresToHandle)
    {
        var earliestNewTime = DateTime.MaxValue;
        var misfireTime = DateTimeOffset.UtcNow - _misfireThreshold;

        var hasMoreMisfiredTriggers = _triggerRepository.HasMisfiredTriggers(misfireTime.UtcDateTime,
            maxMisfiresToHandle, out var misfiredTriggers);

        if (hasMoreMisfiredTriggers)
        {
            Log.Info(
                "Handling the first " + misfiredTriggers.Count +
                " triggers that missed their scheduled fire-time.  " +
                "More misfired triggers remain to be processed.");
        }
        else if (misfiredTriggers.Count > 0)
        {
            Log.Info(
                "Handling " + misfiredTriggers.Count +
                " trigger(s) that missed their scheduled fire-time.");
        }
        else
        {
            Log.Debug(
                "Found 0 triggers that missed their scheduled fire-time.");
            return RecoverMisfiredJobsResult.NoOp;
        }

        foreach (var misfiredTrigger in misfiredTriggers)
        {
            var trigger = await _triggerRepository.GetTrigger(misfiredTrigger).ConfigureAwait(false);

            if (trigger == null)
            {
                continue;
            }

            await DoUpdateOfMisfiredTrigger(trigger, false, Models.TriggerState.Waiting, recovering).ConfigureAwait(false);

            var nextTime = trigger.NextFireTime;
            if (nextTime.HasValue && nextTime.Value < earliestNewTime)
            {
                earliestNewTime = nextTime.Value;
            }
        }

        return new RecoverMisfiredJobsResult(hasMoreMisfiredTriggers, misfiredTriggers.Count, earliestNewTime);
    }

    private async Task DoUpdateOfMisfiredTrigger(Trigger trigger, bool forceState,
        Models.TriggerState newStateIfNotComplete, bool recovering)
    {
        var operableTrigger = (IOperableTrigger)trigger.GetTrigger();

        ICalendar cal = null;
        if (trigger.CalendarName != null)
        {
            cal = (await _calendar_repository.GetCalendar(trigger.CalendarName).ConfigureAwait(false))?.GetCalendar();
        }

        await _schedulerSignaler.NotifyTriggerListenersMisfired(operableTrigger).ConfigureAwait(false);
        operableTrigger.UpdateAfterMisfire(cal);

        // Update trigger state based on whether it has a next fire time
        if (!operableTrigger.GetNextFireTimeUtc().HasValue)
        {
            await _triggerRepository.UpdateTriggerState(operableTrigger.Key, Models.TriggerState.Complete).ConfigureAwait(false);
            await _schedulerSignaler.NotifySchedulerListenersFinalized(operableTrigger).ConfigureAwait(false);
        }
        else
        {
            // Update the trigger model with new fire times
            trigger.NextFireTime = operableTrigger.GetNextFireTimeUtc()?.UtcDateTime;
            trigger.PreviousFireTime = operableTrigger.GetPreviousFireTimeUtc()?.UtcDateTime;
            await _triggerRepository.UpdateTrigger(trigger, CancellationToken.None).ConfigureAwait(false);
            
            if (!recovering)
            {
                await _triggerRepository.UpdateTriggerState(operableTrigger.Key, newStateIfNotComplete).ConfigureAwait(false);
            }
        }
    }

    private async Task<TriggerFiredBundle> TriggerFiredInternal(IOperableTrigger trigger)
    {
        var model = await _triggerRepository.GetTrigger(trigger.Key).ConfigureAwait(false);
        if (model == null)
        {
            throw new JobPersistenceException($"Trigger {trigger.Key} not found");
        }

        var jobModel = await _jobDetailRepository.GetJob(model.JobKey).ConfigureAwait(false);
        var jobDetail = jobModel?.GetJobDetail();
        if (jobDetail == null)
        {
            throw new JobPersistenceException($"Job {model.JobKey} not found for trigger {trigger.Key}");
        }

        ICalendar cal = null;
        if (model.CalendarName != null)
        {
            cal = (await _calendar_repository.GetCalendar(model.CalendarName).ConfigureAwait(false))?.GetCalendar();
        }

        // Update state to executing
        await _triggerRepository.UpdateTriggerState(trigger.Key, Models.TriggerState.Blocked, Models.TriggerState.Acquired).ConfigureAwait(false);

        var firedTime = DateTimeOffset.UtcNow;
        var bundle = new TriggerFiredBundle(jobDetail, trigger, cal, false, firedTime, null, null, null);

        // Record fired trigger
        var firedInstanceId = $"{trigger.Key.Name}:{trigger.Key.Group}:{_instanceId}:{firedTime.UtcTicks}";
        await _firedTriggerRepository.AddFiredTrigger(new FiredTrigger(firedInstanceId, model, jobModel)).ConfigureAwait(false);

        return bundle;
    }

    private async Task TriggeredJobCompleteInternal(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken token)
    {
        switch (triggerInstCode)
        {
            case SchedulerInstruction.DeleteTrigger:
                await _triggerRepository.DeleteTrigger(trigger.Key).ConfigureAwait(false);
                break;
            case SchedulerInstruction.SetTriggerComplete:
                await _triggerRepository.UpdateTriggerState(trigger.Key, Models.TriggerState.Complete, Models.TriggerState.Blocked).ConfigureAwait(false);
                break;
            case SchedulerInstruction.SetTriggerError:
                await _triggerRepository.UpdateTriggerState(trigger.Key, Models.TriggerState.Error, Models.TriggerState.Blocked).ConfigureAwait(false);
                break;
            case SchedulerInstruction.SetAllJobTriggersComplete:
                var keys = await _triggerRepository.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(trigger.Key.Group)).ConfigureAwait(false);
                foreach (var key in keys)
                {
                    await _triggerRepository.UpdateTriggerState(key, Models.TriggerState.Complete).ConfigureAwait(false);
                }
                break;
            default:
                // reset back to waiting
                await _triggerRepository.UpdateTriggerState(trigger.Key, Models.TriggerState.Waiting, Models.TriggerState.Blocked).ConfigureAwait(false);
                break;
        }

        var firedInstanceIdPrefix = $"{trigger.Key.Name}:{trigger.Key.Group}:{_instanceId}";
        var firedList = await _firedTriggerRepository.GetFiredTriggers(_instanceId).ConfigureAwait(false);
        var toDelete = firedList.Where(ft => ft.TriggerKey == trigger.Key && ft.Id.FiredInstanceId.StartsWith(firedInstanceIdPrefix)).ToList();
        foreach (var ft in toDelete)
        {
            await _firedTriggerRepository.DeleteFiredTrigger(ft.Id.FiredInstanceId).ConfigureAwait(false);
        }
    }
}
