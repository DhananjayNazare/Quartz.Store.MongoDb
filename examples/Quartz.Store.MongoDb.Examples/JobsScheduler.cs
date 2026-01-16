using Microsoft.Extensions.Hosting;

namespace Quartz.Store.MongoDb.Examples
{
    internal class JobsScheduler(ISchedulerFactory schedulerFactory) : IHostedService
    {
        private IScheduler? _scheduler;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            var job = JobBuilder.Create<HelloJob>().WithIdentity("HelloJob", "Group1").Build();
            var trigger1 = TriggerBuilder.Create().WithIdentity("HelloTrigger", "Group1").StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever()).Build();

            await _scheduler.ScheduleJob(job, [trigger1], replace:true, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if(_scheduler?.IsShutdown == false)
            {
                await _scheduler.Clear(cancellationToken);
                await _scheduler.Shutdown(cancellationToken);
            }
        }
    }
}
