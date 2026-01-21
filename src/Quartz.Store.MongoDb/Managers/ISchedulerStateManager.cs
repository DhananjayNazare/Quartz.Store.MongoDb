using System;
using System.Collections.Generic;
using System.Threading;
namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages scheduler pause/resume state operations.</summary>
public interface ISchedulerStateManager
{
    /// <summary>Pauses a trigger.</summary>
    Task PauseTrigger(TriggerKey triggerKey, CancellationToken token);

    /// <summary>Pauses multiple triggers matching a group.</summary>
    Task<IReadOnlyCollection<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token);

    /// <summary>Pauses a job and all its triggers.</summary>
    Task PauseJob(JobKey jobKey, CancellationToken token);

    /// <summary>Pauses multiple jobs matching a group.</summary>
    Task<IReadOnlyCollection<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken token);

    /// <summary>Pauses all triggers and jobs.</summary>
    Task PauseAll(CancellationToken token);

    /// <summary>Resumes a trigger.</summary>
    Task ResumeTrigger(TriggerKey triggerKey, CancellationToken token);

    /// <summary>Resumes multiple triggers matching a group.</summary>
    Task<IReadOnlyCollection<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token);

    /// <summary>Resumes a job and all its triggers.</summary>
    Task ResumeJob(JobKey jobKey, CancellationToken token);

    /// <summary>Resumes multiple jobs matching a group.</summary>
    Task<IReadOnlyCollection<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken token);

    /// <summary>Resumes all triggers and jobs.</summary>
    Task ResumeAll(CancellationToken token);

    /// <summary>Gets all paused trigger groups.</summary>
    Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken token);
}
