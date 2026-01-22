using System;
using System.Threading;
using Common.Logging;
using Quartz.Impl.AdoJobStore;
using Quartz.Store.MongoDb.Managers;

namespace Quartz.Store.MongoDb
{
    internal class MisfireHandler : QuartzThread
    {
        private static readonly ILog Log = LogManager.GetLogger<MisfireHandler>();

        private readonly ITriggerFireManager _triggerFireManager;
        private readonly string _instanceName;
        private readonly string _instanceId;
        private readonly TimeSpan _misfireThreshold;
        private readonly TimeSpan _dbRetryInterval;
        private readonly int _retryableActionErrorLogThreshold;
        private bool _shutdown;
        private int _numFails;

        public MisfireHandler(
            ITriggerFireManager triggerFireManager,
            string instanceName,
            string instanceId,
            TimeSpan misfireThreshold,
            TimeSpan dbRetryInterval,
            int retryableActionErrorLogThreshold)
        {
            _triggerFireManager = triggerFireManager ?? throw new ArgumentNullException(nameof(triggerFireManager));
            _instanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
            _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
            _misfireThreshold = misfireThreshold;
            _dbRetryInterval = dbRetryInterval;
            _retryableActionErrorLogThreshold = retryableActionErrorLogThreshold;
            Name = $"QuartzScheduler_{instanceName}-{instanceId}_MisfireHandler";
            IsBackground = true;
        }

        public void Shutdown()
        {
            _shutdown = true;
            Interrupt();
        }

        public override void Run()
        {
            while (!_shutdown)
            {
                var now = DateTime.UtcNow;
                var recoverResult = Manage();
                
                // Note: Signaling scheduling change is handled by the trigger fire manager
                // or would need to be passed as a callback

                if (!_shutdown)
                {
                    var timeToSleep = TimeSpan.FromMilliseconds(50);
                    if (!recoverResult.HasMoreMisfiredTriggers)
                    {
                        timeToSleep = _misfireThreshold - (DateTime.UtcNow - now);
                        if (timeToSleep <= TimeSpan.Zero)
                        {
                            timeToSleep = TimeSpan.FromMilliseconds(50);
                        }

                        if (_numFails > 0)
                        {
                            timeToSleep = _dbRetryInterval > timeToSleep
                                ? _dbRetryInterval
                                : timeToSleep;
                        }
                    }

                    try
                    {
                        Thread.Sleep(timeToSleep);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }
            }
        }

        private RecoverMisfiredJobsResult Manage()
        {
            try
            {
                Log.Debug("Scanning for misfires...");
                var result = _triggerFireManager.DoRecoverMisfires().Result;
                _numFails = 0;
                return result;
            }
            catch (Exception ex)
            {
                if (_numFails % _retryableActionErrorLogThreshold == 0)
                {
                    Log.Error($"Error handling misfires: {ex.Message}", ex);
                }
                _numFails++;
            }

            return RecoverMisfiredJobsResult.NoOp;
        }
    }
}
