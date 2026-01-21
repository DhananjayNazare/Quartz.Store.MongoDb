using System.Linq;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages job storage, retrieval, and deletion operations.</summary>
internal class JobStorageManager : BaseStorageManager, IJobStorageManager
{
    private readonly JobDetailRepository _jobDetailRepository;
    private readonly TriggerRepository _triggerRepository;
    private readonly ISchedulerSignaler _schedulerSignaler;
    private readonly string _instanceName;

    public JobStorageManager(
        JobDetailRepository jobDetailRepository,
        TriggerRepository triggerRepository,
        LockManager lockManager,
        ISchedulerSignaler schedulerSignaler,
        string instanceId,
        string instanceName)
        : base(lockManager, instanceId)
    {
        _jobDetailRepository = jobDetailRepository ?? throw new ArgumentNullException(nameof(jobDetailRepository));
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _schedulerSignaler = schedulerSignaler ?? throw new ArgumentNullException(nameof(schedulerSignaler));
        _instanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    }

    /// <summary>Stores a job detail.</summary>
    public async Task StoreJob(IJobDetail newJob, bool replaceExisting, CancellationToken token)
    {
        await ExecuteWithLock(async () =>
        {
            await StoreJobInternal(newJob, replaceExisting);
        }, token);
    }

    /// <summary>Retrieves a job by its key.</summary>
    public async Task<IJobDetail> RetrieveJob(JobKey jobKey, CancellationToken token)
    {
        var result = await _jobDetailRepository.GetJob(jobKey).ConfigureAwait(false);
        return result?.GetJobDetail();
    }

    /// <summary>Removes a job by its key.</summary>
    public async Task<bool> RemoveJob(JobKey jobKey, CancellationToken token)
    {
        return await ExecuteWithLock(async () =>
        {
            return await RemoveJobInternal(jobKey);
        }, token);
    }

    /// <summary>Removes multiple jobs by their keys.</summary>
    public async Task<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken token)
    {
        return await ExecuteWithLock(async () =>
        {
            foreach (var jobKey in jobKeys)
            {
                token.ThrowIfCancellationRequested();
                var removed = await RemoveJobInternal(jobKey).ConfigureAwait(false);
                if (!removed)
                {
                    return false;
                }
            }

            return true;
        }, token);
    }

    /// <summary>Checks if a job exists.</summary>
    public async Task<bool> CheckExists(JobKey jobKey, CancellationToken token)
    {
        return await _jobDetailRepository.JobExists(jobKey).ConfigureAwait(false);
    }

    /// <summary>Gets the number of jobs in the store.</summary>
    public async Task<int> GetNumberOfJobs(CancellationToken token)
    {
        return (int)await _jobDetailRepository.GetCount().ConfigureAwait(false);
    }

    /// <summary>Gets all job keys matching the given group matcher.</summary>
    public async Task<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken token)
    {
        var keys = await _jobDetailRepository.GetJobsKeys(matcher).ConfigureAwait(false);
        return new HashSet<JobKey>(keys);
    }

    /// <summary>Gets all job group names.</summary>
    public async Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken token)
    {
        var names = await _jobDetailRepository.GetJobGroupNames().ConfigureAwait(false);
        return names.ToList();
    }

    /// <summary>Gets all triggers associated with a job.</summary>
    public async Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken token)
    {
        var result = await _triggerRepository.GetTriggers(jobKey).ConfigureAwait(false);
        return result.Select(trigger => trigger.GetTrigger())
            .Cast<IOperableTrigger>()
            .ToList();
    }

    /// <summary>Internal method to store a job detail.</summary>
    private async Task StoreJobInternal(IJobDetail newJob, bool replaceExisting)
    {
        var existingJob = await _jobDetailRepository.JobExists(newJob.Key).ConfigureAwait(false);

        if (existingJob)
        {
            if (!replaceExisting)
            {
                throw new ObjectAlreadyExistsException(newJob);
            }

            await _jobDetailRepository.UpdateJob(new JobDetail(newJob, _instanceName), true).ConfigureAwait(false);
        }
        else
        {
            await _jobDetailRepository.AddJob(new JobDetail(newJob, _instanceName)).ConfigureAwait(false);
        }
    }

    /// <summary>Internal method to remove a job and its triggers.</summary>
    private async Task<bool> RemoveJobInternal(JobKey jobKey)
    {
        await _triggerRepository.DeleteTriggers(jobKey).ConfigureAwait(false);
        var result = await _jobDetailRepository.DeleteJob(jobKey).ConfigureAwait(false);
        return result > 0;
    }
}
