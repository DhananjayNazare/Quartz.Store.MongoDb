using System;
using System.Threading.Tasks;
using Quartz;

namespace Quartz.Store.MongoDb.Examples.Jobs
{
    /// <summary>
    /// Example job that demonstrates calendar interval scheduling.
    /// This job runs at regular calendar-based intervals (e.g., every 2 weeks).
    /// </summary>
    internal class CalendarIntervalExampleJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] CalendarIntervalExampleJob executed!");
            Console.WriteLine($"  This job runs every 2 weeks");
            Console.WriteLine($"  Job Key: {context.JobDetail.Key}");
            
            // Simulate some work
            await Task.Delay(100);
        }
    }
}
