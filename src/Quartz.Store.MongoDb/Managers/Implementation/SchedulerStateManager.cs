using System.Linq;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using Quartz.Store.MongoDb.Models;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages scheduler pause/resume state operations.</summary>
internal class SchedulerStateManager : BaseStorageManager, ISchedulerStateManager
{
    private readonly TriggerRepository _triggerRepository;
    private readonly PausedTriggerGroupRepository _pausedTriggerGroupRepository;
    private readonly JobDetailRepository _jobDetailRepository;
    private const string AllGroupsPaused = "_$_ALL_GROUPS_PAUSED_$_";

    public SchedulerStateManager(
        TriggerRepository triggerRepository,
        PausedTriggerGroupRepository pausedTriggerGroupRepository,
        JobDetailRepository jobDetailRepository,
        LockManager lockManager,
        string instanceId)
        : base(lockManager, instanceId)
    {
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _pausedTriggerGroupRepository = pausedTriggerGroupRepository ?? throw new ArgumentNullException(nameof(pausedTriggerGroupRepository));
        _jobDetailRepository = jobDetailRepository ?? throw new ArgumentNullException(nameof(jobDetailRepository));
    }

    /// <summary>Pauses a single trigger.</summary>
    public async Task PauseTrigger(TriggerKey triggerKey, CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                await PauseTriggerInternal(triggerKey);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Pauses multiple triggers matching a group.</summary>
    public async Task<IReadOnlyCollection<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                return await PauseTriggerGroupInternal(matcher, token);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Pauses a job and all its triggers.</summary>
    public async Task PauseJob(JobKey jobKey, CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                var triggers = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
                foreach (var operableTrigger in triggers)
                {
                    await PauseTriggerInternal(operableTrigger.GetTrigger().Key).ConfigureAwait(false);
                }
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Pauses multiple jobs matching a group.</summary>
    public async Task<IReadOnlyCollection<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                var jobKeys = await _jobDetailRepository.GetJobsKeys(matcher).ConfigureAwait(false);
                foreach (var jobKey in jobKeys)
                {
                    var triggers = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
                    foreach (var trigger in triggers)
                    {
                        await PauseTriggerInternal(trigger.GetTrigger().Key).ConfigureAwait(false);
                    }
                }

                return jobKeys.Select(key => key.Group).Distinct().ToList();
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Pauses all triggers and jobs.</summary>
    public async Task PauseAll(CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                await PauseAllInternal();
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Resumes a single trigger.</summary>
    public async Task ResumeTrigger(TriggerKey triggerKey, CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                await ResumeTriggerInternal(triggerKey, token);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Resumes multiple triggers matching a group.</summary>
    public async Task<IReadOnlyCollection<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                return await ResumeTriggersInternal(matcher, token);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Resumes a job and all its triggers.</summary>
    public async Task ResumeJob(JobKey jobKey, CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                var triggers = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
                await Task.WhenAll(triggers.Select(trigger =>
                    ResumeTriggerInternal(trigger.GetTrigger().Key, token))).ConfigureAwait(false);
            }, token);
        }
        catch (AggregateException ex)
        {
            throw new JobPersistenceException(ex.InnerExceptions[0].Message, ex.InnerExceptions[0]);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Resumes multiple jobs matching a group.</summary>
    public async Task<IReadOnlyCollection<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                var jobKeys = await _jobDetailRepository.GetJobsKeys(matcher).ConfigureAwait(false);
                foreach (var jobKey in jobKeys)
                {
                    var triggers = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
                    await Task.WhenAll(triggers.Select(trigger =>
                        ResumeTriggerInternal(trigger.GetTrigger().Key, token))).ConfigureAwait(false);
                }

                return new HashSet<string>(jobKeys.Select(key => key.Group));
            }, token);
        }
        catch (AggregateException ex)
        {
            throw new JobPersistenceException(ex.InnerExceptions[0].Message, ex.InnerExceptions[0]);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Resumes all triggers and jobs.</summary>
    public async Task ResumeAll(CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                await ResumeAllInternal();
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Gets all paused trigger groups.</summary>
    public async Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken token)
    {
        var groups = await _pausedTriggerGroupRepository.GetPausedTriggerGroups().ConfigureAwait(false);
        return new HashSet<string>(groups);
    }

    /// <summary>Internal method to pause a single trigger.</summary>
    private async Task PauseTriggerInternal(TriggerKey triggerKey)
    {
        var trigger = await _triggerRepository.GetTrigger(triggerKey).ConfigureAwait(false);
        switch (trigger.State)
        {
            case Models.TriggerState.Waiting:
            case Models.TriggerState.Acquired:
                await _triggerRepository.UpdateTriggerState(triggerKey, Models.TriggerState.Paused).ConfigureAwait(false);
                break;
            case Models.TriggerState.Blocked:
                await _triggerRepository.UpdateTriggerState(triggerKey, Models.TriggerState.PausedBlocked).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>Internal method to pause a trigger group.</summary>
    private async Task<IReadOnlyCollection<string>> PauseTriggerGroupInternal(GroupMatcher<TriggerKey> matcher, CancellationToken token)
    {
        await _triggerRepository.UpdateTriggersStates(matcher, Models.TriggerState.Paused,
            Models.TriggerState.Acquired, Models.TriggerState.Waiting).ConfigureAwait(false);
        await _triggerRepository.UpdateTriggersStates(matcher, Models.TriggerState.PausedBlocked,
            Models.TriggerState.Blocked).ConfigureAwait(false);

        var triggerGroups = await _triggerRepository.GetTriggerGroupNames(matcher).ConfigureAwait(false);

        // Account for exact group match for group that doesn't exist yet
        var op = matcher.CompareWithOperator;
        if (op.Equals(StringOperator.Equality) && !triggerGroups.Contains(matcher.CompareToValue))
        {
            triggerGroups.Add(matcher.CompareToValue);
        }

        foreach (var triggerGroup in triggerGroups)
        {
            if (!await _pausedTriggerGroupRepository.IsTriggerGroupPaused(triggerGroup).ConfigureAwait(false))
            {
                await _pausedTriggerGroupRepository.AddPausedTriggerGroup(triggerGroup).ConfigureAwait(false);
            }
        }

        return new HashSet<string>(triggerGroups);
    }

    /// <summary>Internal method to pause all triggers.</summary>
    private async Task PauseAllInternal()
    {
        var groupNames = await _triggerRepository.GetTriggerGroupNames().ConfigureAwait(false);

        await Task.WhenAll(groupNames.Select(groupName =>
            PauseTriggerGroupInternal(GroupMatcher<TriggerKey>.GroupEquals(groupName), CancellationToken.None))).ConfigureAwait(false);

        if (!await _pausedTriggerGroupRepository.IsTriggerGroupPaused(AllGroupsPaused).ConfigureAwait(false))
        {
            await _pausedTriggerGroupRepository.AddPausedTriggerGroup(AllGroupsPaused).ConfigureAwait(false);
        }
    }

    /// <summary>Internal method to resume a trigger.</summary>
    private async Task ResumeTriggerInternal(TriggerKey triggerKey, CancellationToken token)
    {
        var trigger = await _triggerRepository.GetTrigger(triggerKey).ConfigureAwait(false);
        if (trigger?.NextFireTime == null || trigger.NextFireTime == DateTime.MinValue)
        {
            return;
        }

        var blocked = trigger.State == Models.TriggerState.PausedBlocked;
        var newState = Models.TriggerState.Waiting;

        await _triggerRepository.UpdateTriggerState(triggerKey, newState,
            blocked ? Models.TriggerState.PausedBlocked : Models.TriggerState.Paused).ConfigureAwait(false);
    }

    /// <summary>Internal method to resume a trigger group.</summary>
    private async Task<IReadOnlyCollection<string>> ResumeTriggersInternal(GroupMatcher<TriggerKey> matcher, CancellationToken token)
    {
        await _pausedTriggerGroupRepository.DeletePausedTriggerGroup(matcher).ConfigureAwait(false);
        var groups = new HashSet<string>();

        var keys = await _triggerRepository.GetTriggerKeys(matcher).ConfigureAwait(false);
        foreach (var triggerKey in keys)
        {
            await ResumeTriggerInternal(triggerKey, token).ConfigureAwait(false);
            groups.Add(triggerKey.Group);
        }

        return groups.ToList();
    }

    /// <summary>Internal method to resume all triggers.</summary>
    private async Task ResumeAllInternal()
    {
        var groupNames = await _triggerRepository.GetTriggerGroupNames().ConfigureAwait(false);
        await Task.WhenAll(groupNames.Select(groupName =>
            ResumeTriggersInternal(GroupMatcher<TriggerKey>.GroupEquals(groupName), CancellationToken.None))).ConfigureAwait(false);
        await _pausedTriggerGroupRepository.DeletePausedTriggerGroup(AllGroupsPaused).ConfigureAwait(false);
    }
}
