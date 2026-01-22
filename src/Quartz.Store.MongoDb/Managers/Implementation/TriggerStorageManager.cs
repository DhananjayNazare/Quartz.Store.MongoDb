using System.Linq;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages trigger storage, retrieval, and deletion operations.</summary>
internal class TriggerStorageManager : BaseStorageManager, ITriggerStorageManager
{
    private readonly TriggerRepository _triggerRepository;
    private readonly JobDetailRepository _jobDetailRepository;
    private readonly PausedTriggerGroupRepository _pausedTriggerGroupRepository;
    private readonly string _instanceName;
    private const string AllGroupsPaused = "_$_ALL_GROUPS_PAUSED_$_";

    public TriggerStorageManager(
        TriggerRepository triggerRepository,
        JobDetailRepository jobDetailRepository,
        PausedTriggerGroupRepository pausedTriggerGroupRepository,
        LockManager lockManager,
        string instanceId,
        string instanceName)
        : base(lockManager, instanceId)
    {
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _jobDetailRepository = jobDetailRepository ?? throw new ArgumentNullException(nameof(jobDetailRepository));
        _pausedTriggerGroupRepository = pausedTriggerGroupRepository ?? throw new ArgumentNullException(nameof(pausedTriggerGroupRepository));
        _instanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    }

    /// <summary>Stores a trigger.</summary>
    public async Task StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting, CancellationToken token)
    {
        await ExecuteWithLock(async () =>
        {
            await StoreTriggerInternal(newTrigger, null, replaceExisting, Models.TriggerState.Waiting, false, false, token);
        }, token);
    }

    /// <summary>Retrieves a trigger by its key.</summary>
    public async Task<IOperableTrigger> RetrieveTrigger(TriggerKey triggerKey, CancellationToken token)
    {
        var result = await _triggerRepository.GetTrigger(triggerKey).ConfigureAwait(false);
        return result?.GetTrigger() as IOperableTrigger;
    }

    /// <summary>Removes a trigger by its key.</summary>
    public async Task<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                return await RemoveTriggerInternal(triggerKey);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Removes multiple triggers by their keys.</summary>
    public async Task<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                var allRemoved = true;
                foreach (var triggerKey in triggerKeys)
                {
                    allRemoved = allRemoved && await RemoveTriggerInternal(triggerKey);
                }
                return allRemoved;
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Replaces an existing trigger with a new one.</summary>
    public async Task<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                return await ReplaceTriggerInternal(triggerKey, newTrigger);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Checks if a trigger exists.</summary>
    public async Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken token)
    {
        return await _triggerRepository.TriggerExists(triggerKey).ConfigureAwait(false);
    }

    /// <summary>Gets the number of triggers in the store.</summary>
    public async Task<int> GetNumberOfTriggers(CancellationToken token)
    {
        return (int)await _triggerRepository.GetCount().ConfigureAwait(false);
    }

    /// <summary>Gets all trigger keys matching the given group matcher.</summary>
    public async Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken token)
    {
        var keys = await _triggerRepository.GetTriggerKeys(matcher).ConfigureAwait(false);
        return new HashSet<TriggerKey>(keys);
    }

    /// <summary>Gets all trigger group names.</summary>
    public async Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken token)
    {
        return await _triggerRepository.GetTriggerGroupNames().ConfigureAwait(false);
    }

    /// <summary>Gets the state of a trigger.</summary>
    public async Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken token)
    {
        var trigger = await _triggerRepository.GetTrigger(triggerKey).ConfigureAwait(false);

        if (trigger == null)
        {
            return TriggerState.None;
        }

        switch (trigger.State)
        {
            case Models.TriggerState.Deleted:
                return TriggerState.None;
            case Models.TriggerState.Complete:
                return TriggerState.Complete;
            case Models.TriggerState.Paused:
            case Models.TriggerState.PausedBlocked:
                return TriggerState.Paused;
            case Models.TriggerState.Error:
                return TriggerState.Error;
            case Models.TriggerState.Blocked:
                return TriggerState.Blocked;
            default:
                return TriggerState.Normal;
        }
    }

    /// <summary>Gets all triggers associated with a job.</summary>
    public async Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken token)
    {
        var result = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
        return result.Select(trigger => trigger.GetTrigger())
            .Cast<IOperableTrigger>()
            .ToList();
    }

    /// <summary>Resets a trigger from error state.</summary>
    public Task ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken token)
    {
        // This is a placeholder implementation
        // The actual implementation would reset the trigger state from Error to Normal/Waiting
        return Task.CompletedTask;
    }

    /// <summary>Internal method to store a trigger.</summary>
    private async Task StoreTriggerInternal(
        IOperableTrigger newTrigger,
        IJobDetail job,
        bool replaceExisting,
        Models.TriggerState state,
        bool forceState,
        bool recovering,
        CancellationToken token)
    {
        var existingTrigger = await _triggerRepository.TriggerExists(newTrigger.Key).ConfigureAwait(false);

        if (existingTrigger && !replaceExisting)
        {
            throw new ObjectAlreadyExistsException(newTrigger);
        }

        if (!forceState)
        {
            var shouldBePaused = await _pausedTriggerGroupRepository.IsTriggerGroupPaused(newTrigger.Key.Group).ConfigureAwait(false);

            if (!shouldBePaused)
            {
                shouldBePaused = await _pausedTriggerGroupRepository.IsTriggerGroupPaused(AllGroupsPaused).ConfigureAwait(false);
                if (shouldBePaused)
                {
                    await _pausedTriggerGroupRepository.AddPausedTriggerGroup(newTrigger.Key.Group).ConfigureAwait(false);
                }
            }

            if (shouldBePaused && (state == Models.TriggerState.Waiting || state == Models.TriggerState.Acquired))
            {
                state = Models.TriggerState.Paused;
            }
        }

        if (job == null)
        {
            job = (await _jobDetailRepository.GetJob(newTrigger.JobKey).ConfigureAwait(false))?.GetJobDetail();
        }

        if (job == null)
        {
            throw new JobPersistenceException($"The job ({newTrigger.JobKey}) referenced by the trigger does not exist.");
        }

        if (job.ConcurrentExecutionDisallowed && !recovering)
        {
            state = await CheckBlockedState(job.Key, state).ConfigureAwait(false);
        }

        if (existingTrigger)
        {
            await _triggerRepository.UpdateTrigger(TriggerFactory.CreateTrigger(newTrigger, state, _instanceName)).ConfigureAwait(false);
        }
        else
        {
            await _triggerRepository.AddTrigger(TriggerFactory.CreateTrigger(newTrigger, state, _instanceName)).ConfigureAwait(false);
        }
    }

    /// <summary>Internal method to remove a trigger.</summary>
    private async Task<bool> RemoveTriggerInternal(TriggerKey key, IJobDetail job = null)
    {
        var trigger = await _triggerRepository.GetTrigger(key).ConfigureAwait(false);
        if (trigger == null)
        {
            return false;
        }

        if (job == null)
        {
            var result = await _jobDetailRepository.GetJob(trigger.JobKey).ConfigureAwait(false);
            job = result?.GetJobDetail();
        }

        var removedTrigger = await _triggerRepository.DeleteTrigger(key).ConfigureAwait(false) > 0;
        return removedTrigger;
    }

    /// <summary>Internal method to replace a trigger.</summary>
    private async Task<bool> ReplaceTriggerInternal(TriggerKey triggerKey, IOperableTrigger newTrigger)
    {
        var trigger = await _triggerRepository.GetTrigger(triggerKey).ConfigureAwait(false);
        var result = await _jobDetailRepository.GetJob(trigger.JobKey).ConfigureAwait(false);
        var job = result?.GetJobDetail();

        if (job == null)
        {
            return false;
        }

        if (!newTrigger.JobKey.Equals(job.Key))
        {
            throw new JobPersistenceException("New trigger is not related to the same job as the old trigger.");
        }

        var removedTrigger = await _triggerRepository.DeleteTrigger(triggerKey).ConfigureAwait(false) > 0;
        await StoreTriggerInternal(newTrigger, job, false, Models.TriggerState.Waiting, false, false, CancellationToken.None).ConfigureAwait(false);
        return removedTrigger;
    }

    /// <summary>Internal method to check if trigger should be blocked.</summary>
    private async Task<Models.TriggerState> CheckBlockedState(JobKey jobKey, Models.TriggerState currentState)
    {
        if (currentState != Models.TriggerState.Waiting && currentState != Models.TriggerState.Paused)
        {
            return currentState;
        }

        // Check if job has any fired triggers (indicating concurrent execution)
        // This is a placeholder - full implementation would check FiredTriggerRepository
        return currentState;
    }
}
