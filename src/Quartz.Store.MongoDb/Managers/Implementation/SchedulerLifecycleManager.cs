using System.Globalization;
using System.Linq;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Managers.Implementation;

/// <summary>Manages scheduler initialization, lifecycle, and shutdown.</summary>
internal class SchedulerLifecycleManager : ISchedulerLifecycleManager
{
    private readonly SchedulerRepository _schedulerRepository;
    private readonly JobDetailRepository _jobDetailRepository;
    private readonly TriggerRepository _triggerRepository;
    private readonly CalendarRepository _calendarRepository;
    private readonly FiredTriggerRepository _firedTriggerRepository;
    private readonly PausedTriggerGroupRepository _pausedTriggerGroupRepository;
    private readonly LockManager _lockManager;
    private MisfireHandler _misfireHandler;
    private readonly SchedulerId _schedulerId;
    private readonly string _instanceId;
    private readonly string _instanceName;
    private readonly IJobStorageManager _jobStorageManager;
    private readonly ITriggerStorageManager _triggerStorageManager;
    private readonly ITriggerFireManager _triggerFireManager;
    private bool _schedulerRunning;
    private ISchedulerSignaler _schedulerSignaler;
    private readonly TimeSpan _misfireThreshold;
    private readonly TimeSpan _dbRetryInterval;
    private readonly int _retryableActionErrorLogThreshold;
    private static readonly ILog Log = LogManager.GetLogger<SchedulerLifecycleManager>();

    public SchedulerLifecycleManager(
        SchedulerRepository schedulerRepository,
        JobDetailRepository jobDetailRepository,
        TriggerRepository triggerRepository,
        CalendarRepository calendarRepository,
        FiredTriggerRepository firedTriggerRepository,
        PausedTriggerGroupRepository pausedTriggerGroupRepository,
        LockManager lockManager,
        IJobStorageManager jobStorageManager,
        ITriggerStorageManager triggerStorageManager,
        ITriggerFireManager triggerFireManager,
        string instanceId,
        string instanceName,
        TimeSpan misfireThreshold,
        TimeSpan dbRetryInterval,
        int retryableActionErrorLogThreshold)
    {
        _schedulerRepository = schedulerRepository ?? throw new ArgumentNullException(nameof(schedulerRepository));
        _jobDetailRepository = jobDetailRepository ?? throw new ArgumentNullException(nameof(jobDetailRepository));
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _calendarRepository = calendarRepository ?? throw new ArgumentNullException(nameof(calendarRepository));
        _firedTriggerRepository = firedTriggerRepository ?? throw new ArgumentNullException(nameof(firedTriggerRepository));
        _pausedTriggerGroupRepository = pausedTriggerGroupRepository ?? throw new ArgumentNullException(nameof(pausedTriggerGroupRepository));
        _lockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        _jobStorageManager = jobStorageManager ?? throw new ArgumentNullException(nameof(jobStorageManager));
        _triggerStorageManager = triggerStorageManager ?? throw new ArgumentNullException(nameof(triggerStorageManager));
        _triggerFireManager = triggerFireManager ?? throw new ArgumentNullException(nameof(triggerFireManager));
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _instanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
        _schedulerId = new SchedulerId(instanceId, instanceName);
        _misfireThreshold = misfireThreshold;
        _dbRetryInterval = dbRetryInterval;
        _retryableActionErrorLogThreshold = retryableActionErrorLogThreshold;
    }

    public Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken token)
    {
        _schedulerSignaler = signaler ?? throw new ArgumentNullException(nameof(signaler));
        Log.Trace($"Scheduler {_schedulerId} initialize");

        // Repositories are already initialized via constructor injection
        // This method is called by Quartz to provide the signaler
        
        return Task.FromResult(true);
    }

    public async Task SchedulerStarted(CancellationToken token)
    {
        Log.Trace($"Scheduler {_schedulerId} started");
        
        await _schedulerRepository.AddScheduler(new Scheduler
        {
            Id = _schedulerId,
            State = SchedulerState.Started,
            LastCheckIn = DateTime.UtcNow
        }).ConfigureAwait(false);

        try
        {
            await RecoverJobs(token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new SchedulerConfigException("Failure occurred during job recovery", ex);
        }

        // Create and start misfire handler
        _misfireHandler = new MisfireHandler(
            _triggerFireManager,
            _instanceName,
            _instanceId,
            _misfireThreshold,
            _dbRetryInterval,
            _retryableActionErrorLogThreshold);
        _misfireHandler.Start();
        _schedulerRunning = true;
    }

    public async Task SchedulerPaused(CancellationToken token)
    {
        Log.Trace($"Scheduler {_schedulerId} paused");
        await _schedulerRepository.UpdateState(_schedulerId.Id, SchedulerState.Paused).ConfigureAwait(false);
        _schedulerRunning = false;
    }

    public async Task SchedulerResumed(CancellationToken token)
    {
        Log.Trace($"Scheduler {_schedulerId} resumed");
        await _schedulerRepository.UpdateState(_schedulerId.Id, SchedulerState.Resumed).ConfigureAwait(false);
        _schedulerRunning = true;
    }

    public async Task Shutdown(CancellationToken token)
    {
        Log.Trace($"Scheduler {_schedulerId} shutdown");
        
        if (_misfireHandler != null)
        {
            _misfireHandler.Shutdown();
            try
            {
                _misfireHandler.Join();
            }
            catch (ThreadInterruptedException)
            {
                // Expected during shutdown
            }
        }

        await _schedulerRepository.DeleteScheduler(_schedulerId.Id).ConfigureAwait(false);
    }

    public async Task ClearAllSchedulingData(CancellationToken token)
    {
        try
        {
            using (await _lockManager.AcquireLock(LockType.TriggerAccess, _instanceId).ConfigureAwait(false))
            {
                await _calendarRepository.DeleteAll().ConfigureAwait(false);
                await _firedTriggerRepository.DeleteAll().ConfigureAwait(false);
                await _jobDetailRepository.DeleteAll().ConfigureAwait(false);
                await _pausedTriggerGroupRepository.DeleteAll().ConfigureAwait(false);
                await _schedulerRepository.DeleteAll().ConfigureAwait(false);
                await _triggerRepository.DeleteAll().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    private async Task RecoverJobs(CancellationToken token)
    {
        using (await _lockManager.AcquireLock(LockType.TriggerAccess, _instanceId).ConfigureAwait(false))
        {
            await RecoverJobsInternal(token).ConfigureAwait(false);
        }
    }

    private async Task RecoverJobsInternal(CancellationToken token)
    {
        // Free triggers stuck in Acquired/Blocked state
        var result = await _triggerRepository.UpdateTriggersStates(Models.TriggerState.Waiting,
            Models.TriggerState.Acquired, Models.TriggerState.Blocked).ConfigureAwait(false);
        result += await _triggerRepository.UpdateTriggersStates(Models.TriggerState.Paused,
            Models.TriggerState.PausedBlocked).ConfigureAwait(false);

        Log.Info("Freed " + result + " triggers from 'acquired' / 'blocked' state.");

        // Recover misfired jobs
        await _triggerFireManager.RecoverMisfiredJobs(true, token).ConfigureAwait(false);

        // Recover jobs that were executing when the scheduler shut down
        var results = (await _firedTriggerRepository.GetRecoverableFiredTriggers(_instanceId).ConfigureAwait(false))
            .Select(async trigger =>
                trigger.GetRecoveryTrigger(await _triggerRepository.GetTriggerJobDataMap(trigger.TriggerKey).ConfigureAwait(false)));
        var recoveringJobTriggers = (await Task.WhenAll(results).ConfigureAwait(false)).ToList();

        Log.Info("Recovering " + recoveringJobTriggers.Count +
                 " jobs that were in-progress at the time of the last shut-down.");

        foreach (var recoveringJobTrigger in recoveringJobTriggers)
        {
            if (await _jobDetailRepository.JobExists(recoveringJobTrigger.JobKey).ConfigureAwait(false))
            {
                recoveringJobTrigger.ComputeFirstFireTimeUtc(null);
                // Store recovery trigger using trigger storage manager
                await _triggerStorageManager.StoreTrigger(recoveringJobTrigger, false, token).ConfigureAwait(false);
            }
        }

        Log.Info("Recovery complete");

        // Remove completed triggers
        var completedTriggers =
            await _triggerRepository.GetTriggerKeys(Models.TriggerState.Complete).ConfigureAwait(false);
        foreach (var completedTrigger in completedTriggers)
        {
            await _triggerStorageManager.RemoveTrigger(completedTrigger, token).ConfigureAwait(false);
        }

        Log.Info(string.Format(CultureInfo.InvariantCulture, "Removed {0} 'complete' triggers.",
            completedTriggers.Count));

        // Clean up fired trigger records
        result = await _firedTriggerRepository.DeleteFiredTriggersByInstanceId(_instanceId).ConfigureAwait(false);
        Log.Info("Removed " + result + " stale fired job entries.");
    }
}
