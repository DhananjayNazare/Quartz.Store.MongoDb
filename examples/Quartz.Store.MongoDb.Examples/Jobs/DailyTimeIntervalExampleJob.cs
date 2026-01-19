using System;
using System.Threading.Tasks;
using Quartz;

namespace Quartz.Store.MongoDb.Examples.Jobs
{
    /// <summary>
    /// Example job that demonstrates daily time interval scheduling.
    /// This job runs multiple times per day during a specified time window.
    /// </summary>
    internal class DailyTimeIntervalExampleJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] DailyTimeIntervalExampleJob executed!");
            Console.WriteLine($"  Job will repeat multiple times during business hours (09:00-17:00)");
            Console.WriteLine($"  Fire Count: {context.FireInstanceId}");
            
            // Simulate some work
            await Task.Delay(100);
        }
    }
}
