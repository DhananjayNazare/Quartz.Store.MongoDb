using System;
using System.Collections.Generic;
using System.Threading;
namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages trigger firing, acquisition, and misfire recovery.</summary>
public interface ITriggerFireManager
{
    /// <summary>Acquires the next triggers to be fired within the specified time window.</summary>
    Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken token);

    /// <summary>Releases an acquired trigger back to waiting state.</summary>
    Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken token);

    /// <summary>Processes fired triggers and returns results.</summary>
    Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken token);

    /// <summary>Handles job completion and processes trigger instructions.</summary>
    Task TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken token);

    /// <summary>Recovers misfired triggers and returns recovery results.</summary>
    Task<RecoverMisfiredJobsResult> DoRecoverMisfires();
}
