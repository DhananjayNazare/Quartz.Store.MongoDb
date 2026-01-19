using System;
using System.Threading.Tasks;
using Quartz;

namespace Quartz.Store.MongoDb.Examples.Jobs
{
    /// <summary>
    /// Example job that demonstrates cron-based scheduling.
    /// This job runs at specific times defined by a cron expression.
    /// </summary>
    internal class CronExampleJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] CronExampleJob executed!");
            Console.WriteLine($"  Trigger: {context.Trigger.Key}");
            Console.WriteLine($"  Next Fire Time: {context.NextFireTimeUtc:yyyy-MM-dd HH:mm:ss}");
            await Task.CompletedTask;
        }
    }
}
