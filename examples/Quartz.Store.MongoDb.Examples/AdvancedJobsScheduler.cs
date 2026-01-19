using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Quartz;
using Quartz.Store.MongoDb.Examples.Jobs;

namespace Quartz.Store.MongoDb.Examples
{
    /// <summary>
    /// Advanced job scheduler demonstrating multiple trigger types and advanced patterns.
    /// This scheduler shows:
    /// - Cron-based scheduling (e.g., "0 0 * * * ?" = every day at midnight)
    /// - Simple scheduling (repeating at fixed intervals)
    /// - Calendar interval scheduling (every N days/weeks/months)
    /// - Daily time interval scheduling (specific times each day)
    /// - Job data map usage for passing parameters
    /// - Multiple triggers on one job
    /// </summary>
    internal class AdvancedJobsScheduler(ISchedulerFactory schedulerFactory) : IHostedService
    {
        private IScheduler? _scheduler;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _scheduler = await schedulerFactory.GetScheduler(cancellationToken);

            // Example 1: Cron Trigger - runs every weekday at 9:00 AM
            await ScheduleCronJob(cancellationToken);

            // Example 2: Simple Trigger with JobDataMap - runs every 10 seconds
            await ScheduleJobWithDataMap(cancellationToken);

            // Example 3: Calendar Interval Trigger - runs every 2 weeks
            await ScheduleCalendarIntervalJob(cancellationToken);

            // Example 4: Daily Time Interval Trigger - runs 8 times between 9 AM and 5 PM
            await ScheduleDailyTimeIntervalJob(cancellationToken);

            // Example 5: Job with multiple triggers
            await ScheduleJobWithMultipleTriggers(cancellationToken);

            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Advanced scheduler started with 5 scheduling examples");
            Console.WriteLine("Jobs scheduled:");
            Console.WriteLine("  1. Cron: Every weekday at 9:00 AM");
            Console.WriteLine("  2. Simple: Every 10 seconds with data map");
            Console.WriteLine("  3. Calendar Interval: Every 2 weeks");
            Console.WriteLine("  4. Daily Time Interval: 8 times daily (9 AM - 5 PM)");
            Console.WriteLine("  5. Single job with multiple triggers");
        }

        private async Task ScheduleCronJob(CancellationToken cancellationToken)
        {
            var job = JobBuilder
                .Create<CronExampleJob>()
                .WithIdentity("CronJob", "CronGroup")
                .WithDescription("Executes every weekday at 9:00 AM")
                .Build();

            // Cron expression: 0 0 9 ? * MON-FRI (9 AM on weekdays)
            var trigger = TriggerBuilder
                .Create()
                .WithIdentity("CronTrigger", "CronGroup")
                .WithCronSchedule("0 0 9 ? * MON-FRI")
                .Build();

            await _scheduler.ScheduleJob(job, new List<ITrigger> { trigger }, replace: true, cancellationToken);
        }

        private async Task ScheduleJobWithDataMap(CancellationToken cancellationToken)
        {
            // Create a JobDataMap with custom data
            var jobDataMap = new JobDataMap
            {
                { "JobName", "Email Notification Job" },
                { "ExecutionCount", 0 },
                { "DepartmentId", 42L }
            };

            var job = JobBuilder
                .Create<DataMapExampleJob>()
                .WithIdentity("DataMapJob", "DataMapGroup")
                .WithDescription("Job that uses JobDataMap for parameters")
                .SetJobData(jobDataMap)
                .Build();

            var trigger = TriggerBuilder
                .Create()
                .WithIdentity("DataMapTrigger", "DataMapGroup")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(10)
                    .RepeatForever())
                .Build();

            await _scheduler.ScheduleJob(job, new List<ITrigger> { trigger }, replace: true, cancellationToken);
        }

        private async Task ScheduleCalendarIntervalJob(CancellationToken cancellationToken)
        {
            var job = JobBuilder
                .Create<CalendarIntervalExampleJob>()
                .WithIdentity("CalendarIntervalJob", "CalendarIntervalGroup")
                .WithDescription("Runs every 2 weeks")
                .Build();

            var trigger = TriggerBuilder
                .Create()
                .WithIdentity("CalendarIntervalTrigger", "CalendarIntervalGroup")
                .StartNow()
                .WithCalendarIntervalSchedule(x => x
                    .WithIntervalInWeeks(2))
                .Build();

            await _scheduler.ScheduleJob(job, new List<ITrigger> { trigger }, replace: true, cancellationToken);
        }

        private async Task ScheduleDailyTimeIntervalJob(CancellationToken cancellationToken)
        {
            var job = JobBuilder
                .Create<DailyTimeIntervalExampleJob>()
                .WithIdentity("DailyTimeIntervalJob", "DailyTimeGroup")
                .WithDescription("Runs during business hours")
                .Build();

            var trigger = TriggerBuilder
                .Create()
                .WithIdentity("DailyTimeIntervalTrigger", "DailyTimeGroup")
                .StartNow()
                .WithDailyTimeIntervalSchedule(x => x
                    .OnMondayThroughFriday()
                    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(9, 0))
                    .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0))
                    .WithIntervalInHours(1))
                .Build();

            await _scheduler.ScheduleJob(job, new List<ITrigger> { trigger }, replace: true, cancellationToken);
        }

        private async Task ScheduleJobWithMultipleTriggers(CancellationToken cancellationToken)
        {
            // A single job can have multiple triggers
            var job = JobBuilder
                .Create<HelloJob>()
                .WithIdentity("MultiTriggerJob", "MultiTriggerGroup")
                .WithDescription("Job with multiple triggers")
                .StoreDurably()
                .Build();

            // Trigger 1: Every 15 seconds
            var trigger1 = TriggerBuilder
                .Create()
                .WithIdentity("MultiTrigger-FastInterval", "MultiTriggerGroup")
                .StartNow()
                .WithSimpleSchedule(x => x
                    .WithIntervalInSeconds(15)
                    .RepeatForever())
                .Build();

            // Trigger 2: Daily at 6:00 PM (18:00)
            var trigger2 = TriggerBuilder
                .Create()
                .WithIdentity("MultiTrigger-Daily", "MultiTriggerGroup")
                .WithCronSchedule("0 0 18 * * ?")
                .Build();

            var triggers = new List<ITrigger> { trigger1, trigger2 };
            await _scheduler.ScheduleJob(job, triggers, replace: true, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_scheduler?.IsShutdown == false)
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Shutting down scheduler...");
                await _scheduler.Clear(cancellationToken);
                await _scheduler.Shutdown(cancellationToken);
            }
        }
    }
}
