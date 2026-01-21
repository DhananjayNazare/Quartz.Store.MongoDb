namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages scheduler initialization, state transitions, and shutdown.</summary>
public interface ISchedulerLifecycleManager
{
    /// <summary>Initializes the job store and creates MongoDB connections.</summary>
    Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken token);

    /// <summary>Called when the scheduler has started.</summary>
    Task SchedulerStarted(CancellationToken token);

    /// <summary>Called when the scheduler is paused.</summary>
    Task SchedulerPaused(CancellationToken token);

    /// <summary>Called when the scheduler is resumed.</summary>
    Task SchedulerResumed(CancellationToken token);

    /// <summary>Called when the scheduler is shutting down.</summary>
    Task Shutdown(CancellationToken token);

    /// <summary>Clears all scheduling data from the job store.</summary>
    Task ClearAllSchedulingData(CancellationToken token);
}
