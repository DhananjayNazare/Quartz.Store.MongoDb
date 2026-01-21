using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        // Minimal stub: In full implementation, this would run MisfireHandler.
        // Return placeholder indicating none recovered.
        return await Task.FromResult(RecoverMisfiredJobsResult.NoOp);
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
