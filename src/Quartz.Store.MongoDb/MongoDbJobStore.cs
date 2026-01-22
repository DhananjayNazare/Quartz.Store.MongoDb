using Quartz.Store.MongoDb.Managers;
using Quartz.Store.MongoDb.Managers.Implementation;

namespace Quartz.Store.MongoDb
{
    /// <summary>
    /// MongoDB-backed job store facade that delegates operations to semantic managers.
    /// </summary>
    /// <remarks>
    /// This facade class delegates all IJobStore operations to specialized managers:
    /// - IJobStorageManager: Job CRUD operations
    /// - ITriggerStorageManager: Trigger CRUD operations
    /// - ICalendarStorageManager: Calendar operations
    /// - ISchedulerStateManager: Pause/Resume operations
    /// - ITriggerFireManager: Trigger acquisition and firing
    /// - ISchedulerLifecycleManager: Initialization and shutdown
    /// </remarks>
    public class MongoDbJobStore : IJobStore
    {
        private static readonly ILog Log = LogManager.GetLogger<MongoDbJobStore>();

        private IJobStorageManager _jobStorageManager;
        private ITriggerStorageManager _triggerStorageManager;
        private ICalendarStorageManager _calendarStorageManager;
        private ISchedulerStateManager _schedulerStateManager;
        private ITriggerFireManager _triggerFireManager;
        private ISchedulerLifecycleManager _lifecycleManager;

        static MongoDbJobStore()
        {
            JobStoreClassMap.RegisterClassMaps();
        }

        /// <summary>
        /// Creates a new MongoDbJobStore (used by Quartz.NET via reflection).
        /// </summary>
        /// <remarks>
        /// Managers are created during the Initialize() call after properties are set.
        /// </remarks>
        public MongoDbJobStore()
        {
            MaxMisfiresToHandleAtATime = 20;
            RetryableActionErrorLogThreshold = 4;
            DbRetryInterval = TimeSpan.FromSeconds(15);
            MisfireThreshold = TimeSpan.FromMinutes(1);
        }

        #region Configuration Properties

        /// <summary>MongoDB connection string</summary>
        public string ConnectionString { get; set; }

        /// <summary>Collection name prefix</summary>
        public string CollectionPrefix { get; set; }

        /// <summary>Maximum number of misfired triggers to handle at one time</summary>
        public int MaxMisfiresToHandleAtATime { get; set; }

        /// <summary>Database retry interval</summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan DbRetryInterval { get; set; }

        /// <summary>Misfire threshold</summary>
        private TimeSpan _misfireThreshold;
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan MisfireThreshold
        {
            get => _misfireThreshold;
            set
            {
                if (value.TotalMilliseconds < 1)
                {
                    throw new ArgumentException("MisfireThreshold must be larger than 0");
                }
                _misfireThreshold = value;
            }
        }

        /// <summary>Number of retries before error is logged for recovery operations</summary>
        public int RetryableActionErrorLogThreshold { get; set; }

        /// <summary>Indicates whether this job store supports persistence</summary>
        public bool SupportsPersistence => true;

        /// <summary>Estimated time to release and acquire trigger (milliseconds)</summary>
        public long EstimatedTimeToReleaseAndAcquireTrigger => 200;

        /// <summary>Indicates whether this job store is clustered</summary>
        public bool Clustered => false;

        /// <summary>Scheduler instance ID</summary>
        public string InstanceId { get; set; }

        /// <summary>Scheduler instance name</summary>
        public string InstanceName { get; set; }

        /// <summary>Thread pool size</summary>
        public int ThreadPoolSize { get; set; }

        #endregion

        #region Lifecycle Methods

        public Task Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken token = default)
        {
            Log.Info($"Initializing MongoDbJobStore for instance {InstanceName}/{InstanceId}");
            
            if (string.IsNullOrEmpty(ConnectionString))
            {
                throw new SchedulerConfigException("ConnectionString property must be set before Initialize is called.");
            }

            if (string.IsNullOrEmpty(InstanceId))
            {
                throw new SchedulerConfigException("InstanceId property must be set before Initialize is called.");
            }

            if (string.IsNullOrEmpty(InstanceName))
            {
                throw new SchedulerConfigException("InstanceName property must be set before Initialize is called.");
            }

            // Create MongoDB client and database
            var url = new MongoUrl(ConnectionString);
            var client = new MongoClient(ConnectionString);
            var database = client.GetDatabase(url.DatabaseName);

            // Create repositories
            var schedulerRepository = new SchedulerRepository(database, InstanceName, CollectionPrefix);
            var jobDetailRepository = new JobDetailRepository(database, InstanceName, CollectionPrefix);
            var triggerRepository = new TriggerRepository(database, InstanceName, CollectionPrefix);
            var calendarRepository = new CalendarRepository(database, InstanceName, CollectionPrefix);
            var firedTriggerRepository = new FiredTriggerRepository(database, InstanceName, CollectionPrefix);
            var pausedTriggerGroupRepository = new PausedTriggerGroupRepository(database, InstanceName, CollectionPrefix);
            var lockManager = new LockManager(database, InstanceName, CollectionPrefix);

            // Create managers with dependencies
            _jobStorageManager = new JobStorageManager(
                jobDetailRepository,
                triggerRepository,
                lockManager,
                signaler,
                InstanceId,
                InstanceName);

            _triggerStorageManager = new TriggerStorageManager(
                triggerRepository,
                jobDetailRepository,
                pausedTriggerGroupRepository,
                lockManager,
                InstanceId,
                InstanceName);

            _calendarStorageManager = new CalendarStorageManager(
                calendarRepository,
                triggerRepository,
                lockManager,
                InstanceId,
                InstanceName);

            _schedulerStateManager = new SchedulerStateManager(
                triggerRepository,
                pausedTriggerGroupRepository,
                jobDetailRepository,
                lockManager,
                InstanceId);

            _triggerFireManager = new TriggerFireManager(
                triggerRepository,
                jobDetailRepository,
                firedTriggerRepository,
                calendarRepository,
                lockManager,
                signaler,
                InstanceId,
                MisfireThreshold);

            _lifecycleManager = new SchedulerLifecycleManager(
                schedulerRepository,
                jobDetailRepository,
                triggerRepository,
                calendarRepository,
                firedTriggerRepository,
                pausedTriggerGroupRepository,
                lockManager,
                _jobStorageManager,
                _triggerStorageManager,
                _triggerFireManager,
                InstanceId,
                InstanceName,
                MisfireThreshold,
                DbRetryInterval,
                RetryableActionErrorLogThreshold);

            // Initialize the lifecycle manager
            return _lifecycleManager.Initialize(loadHelper, signaler, token);
        }

        public Task SchedulerStarted(CancellationToken token = default)
            => _lifecycleManager.SchedulerStarted(token);

        public Task SchedulerPaused(CancellationToken token = default)
            => _lifecycleManager.SchedulerPaused(token);

        public Task SchedulerResumed(CancellationToken token = default)
            => _lifecycleManager.SchedulerResumed(token);

        public Task Shutdown(CancellationToken token = default)
            => _lifecycleManager.Shutdown(token);

        public Task ClearAllSchedulingData(CancellationToken token = default)
            => _lifecycleManager.ClearAllSchedulingData(token);

        #endregion

        #region Job Storage Methods

        public async Task StoreJobAndTrigger(IJobDetail newJob, IOperableTrigger newTrigger, CancellationToken token = default)
        {
            // Coordinate between job and trigger storage
            await _jobStorageManager.StoreJob(newJob, false, token);
            await _triggerStorageManager.StoreTrigger(newTrigger, false, token);
        }

        public Task StoreJob(IJobDetail newJob, bool replaceExisting, CancellationToken token = default)
            => _jobStorageManager.StoreJob(newJob, replaceExisting, token);

        public async Task StoreJobsAndTriggers(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, bool replace, CancellationToken token = default)
        {
            // Coordinate between job and trigger storage
            foreach (var job in triggersAndJobs.Keys)
            {
                await _jobStorageManager.StoreJob(job, replace, token);
                foreach (var trigger in triggersAndJobs[job])
                {
                    await _triggerStorageManager.StoreTrigger((IOperableTrigger)trigger, replace, token);
                }
            }
        }

        public Task<bool> RemoveJob(JobKey jobKey, CancellationToken token = default)
            => _jobStorageManager.RemoveJob(jobKey, token);

        public Task<bool> RemoveJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken token = default)
            => _jobStorageManager.RemoveJobs(jobKeys, token);

        public Task<IJobDetail> RetrieveJob(JobKey jobKey, CancellationToken token = default)
            => _jobStorageManager.RetrieveJob(jobKey, token);

        public Task<bool> CheckExists(JobKey jobKey, CancellationToken token = default)
            => _jobStorageManager.CheckExists(jobKey, token);

        public Task<int> GetNumberOfJobs(CancellationToken token = default)
            => _jobStorageManager.GetNumberOfJobs(token);

        public Task<IReadOnlyCollection<JobKey>> GetJobKeys(GroupMatcher<JobKey> matcher, CancellationToken token = default)
            => _jobStorageManager.GetJobKeys(matcher, token);

        public Task<IReadOnlyCollection<string>> GetJobGroupNames(CancellationToken token = default)
            => _jobStorageManager.GetJobGroupNames(token);

        #endregion

        #region Trigger Storage Methods

        public Task StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting, CancellationToken token = default)
            => _triggerStorageManager.StoreTrigger(newTrigger, replaceExisting, token);

        public Task<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken token = default)
            => _triggerStorageManager.RemoveTrigger(triggerKey, token);

        public Task<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken token = default)
            => _triggerStorageManager.RemoveTriggers(triggerKeys, token);

        public Task<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken token = default)
            => _triggerStorageManager.ReplaceTrigger(triggerKey, newTrigger, token);

        public Task<IOperableTrigger> RetrieveTrigger(TriggerKey triggerKey, CancellationToken token = default)
            => _triggerStorageManager.RetrieveTrigger(triggerKey, token);

        public Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken token = default)
            => _triggerStorageManager.CheckExists(triggerKey, token);

        public Task<int> GetNumberOfTriggers(CancellationToken token = default)
            => _triggerStorageManager.GetNumberOfTriggers(token);

        public Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken token = default)
            => _triggerStorageManager.GetTriggerKeys(matcher, token);

        public Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken token = default)
            => _triggerStorageManager.GetTriggerGroupNames(token);

        public Task<IReadOnlyCollection<IOperableTrigger>> GetTriggersForJob(JobKey jobKey, CancellationToken token = default)
            => _triggerStorageManager.GetTriggersForJob(jobKey, token);

        public Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken token = default)
            => _triggerStorageManager.GetTriggerState(triggerKey, token);

        public Task ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken token = default)
            => _triggerStorageManager.ResetTriggerFromErrorState(triggerKey, token);

        #endregion

        #region Calendar Storage Methods

        public Task StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken token = default)
            => _calendarStorageManager.StoreCalendar(name, calendar, replaceExisting, updateTriggers, token);

        public Task<bool> RemoveCalendar(string calName, CancellationToken token = default)
            => _calendarStorageManager.RemoveCalendar(calName, token);

        public Task<ICalendar> RetrieveCalendar(string calName, CancellationToken token = default)
            => _calendarStorageManager.RetrieveCalendar(calName, token);

        public Task<bool> CalendarExists(string calName, CancellationToken token = default)
            => _calendarStorageManager.CalendarExists(calName, token);

        public Task<int> GetNumberOfCalendars(CancellationToken token = default)
            => _calendarStorageManager.GetNumberOfCalendars(token);

        public Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken token = default)
            => _calendarStorageManager.GetCalendarNames(token);

        #endregion

        #region Scheduler State Methods

        public Task PauseTrigger(TriggerKey triggerKey, CancellationToken token = default)
            => _schedulerStateManager.PauseTrigger(triggerKey, token);

        public Task<IReadOnlyCollection<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token = default)
            => _schedulerStateManager.PauseTriggers(matcher, token);

        public Task PauseJob(JobKey jobKey, CancellationToken token = default)
            => _schedulerStateManager.PauseJob(jobKey, token);

        public Task<IReadOnlyCollection<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken token = default)
            => _schedulerStateManager.PauseJobs(matcher, token);

        public Task ResumeTrigger(TriggerKey triggerKey, CancellationToken token = default)
            => _schedulerStateManager.ResumeTrigger(triggerKey, token);

        public Task<IReadOnlyCollection<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken token = default)
            => _schedulerStateManager.ResumeTriggers(matcher, token);

        public Task ResumeJob(JobKey jobKey, CancellationToken token = default)
            => _schedulerStateManager.ResumeJob(jobKey, token);

        public Task<IReadOnlyCollection<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken token = default)
            => _schedulerStateManager.ResumeJobs(matcher, token);

        public Task<IReadOnlyCollection<string>> GetPausedTriggerGroups(CancellationToken token = default)
            => _schedulerStateManager.GetPausedTriggerGroups(token);

        public Task PauseAll(CancellationToken token = default)
            => _schedulerStateManager.PauseAll(token);

        public Task ResumeAll(CancellationToken token = default)
            => _schedulerStateManager.ResumeAll(token);

        #endregion

        #region Trigger Fire Methods

        public Task<IReadOnlyCollection<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken token = default)
            => _triggerFireManager.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, token);

        public Task ReleaseAcquiredTrigger(IOperableTrigger trigger, CancellationToken token = default)
            => _triggerFireManager.ReleaseAcquiredTrigger(trigger, token);

        public Task<IReadOnlyCollection<TriggerFiredResult>> TriggersFired(IReadOnlyCollection<IOperableTrigger> triggers, CancellationToken token = default)
            => _triggerFireManager.TriggersFired(triggers, token);

        public Task TriggeredJobComplete(IOperableTrigger trigger, IJobDetail jobDetail, SchedulerInstruction triggerInstCode, CancellationToken token = default)
            => _triggerFireManager.TriggeredJobComplete(trigger, jobDetail, triggerInstCode, token);

        #endregion

        #region Not Implemented (Not Supported)

        public Task<bool> IsJobGroupPaused(string groupName, CancellationToken token = default)
        {
            throw new NotImplementedException("IsJobGroupPaused is not implemented");
        }

        public Task<bool> IsTriggerGroupPaused(string groupName, CancellationToken token = default)
        {
            throw new NotImplementedException("IsTriggerGroupPaused is not implemented");
        }

        #endregion
    }
}
