namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages job storage, retrieval, and deletion operations.</summary>
public interface IJobStorageManager
{
    /// <summary>Stores a job detail.</summary>
    Task StoreJob(IJobDetail newJob, bool replaceExisting, CancellationToken token);

    /// <summary>Retrieves a job by its key.</summary>
    Task<IJobDetail> RetrieveJob(JobKey jobKey, CancellationToken token);

    /// <summary>Removes a job by its key.</summary>
    Task<bool> RemoveJob(JobKey jobKey, CancellationToken token);

    /// <summary>Removes multiple jobs by their keys.</summary>
    Task<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken token);

    /// <summary>Checks if a job exists.</summary>
    Task<bool> CheckExists(JobKey jobKey, CancellationToken token);

    /// <summary>Gets the number of jobs in the store.</summary>
    Task<int> GetNumberOfJobs(CancellationToken token);

    /// <summary>Gets all job keys matching the given group matcher.</summary>
    Task<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken token);

    /// <summary>Gets all job group names.</summary>
    Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken token);
}
