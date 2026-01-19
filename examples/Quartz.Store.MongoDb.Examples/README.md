# Quartz.Store.MongoDb Examples

This directory contains example applications demonstrating how to use the Quartz.Store.MongoDb job store.

## Overview

The examples show:

- Basic job scheduling with Quartz.NET and MongoDB persistence
- Advanced scheduling patterns (Cron, Simple, Calendar Interval, Daily Time Interval)
- Job data map usage for passing parameters to jobs
- Multiple triggers on a single job
- Graceful shutdown patterns

## Running the Examples

### Prerequisites

- .NET 9
- MongoDB running on localhost:27017 (or update `appSettings.json` with your connection string)

### Configuration

Update `appSettings.json` with your MongoDB connection details:

```json
{
  "Quartz": {
    "quartz.scheduler.instanceName": "TestScheduler",
    "quartz.scheduler.instanceId": "AUTO",
    "quartz.jobStore.clustered": "true",
    "quartz.jobStore.connectionString": "mongodb://localhost:27017/quartz",
    "quartz.jobStore.collectionPrefix": "Quartz"
  }
}
```

### Run Basic Example

```bash
dotnet run
```

This starts the `JobsScheduler` which demonstrates:

- A simple job (`HelloJob`) that prints to console every 5 seconds
- How to set up dependency injection with the generic host
- Graceful shutdown handling

### Run Advanced Examples

To run the advanced scheduler with multiple scheduling patterns, modify `Program.cs` to use `AdvancedJobsScheduler` instead of `JobsScheduler`:

```csharp
services.AddHostedService<AdvancedJobsScheduler>();  // Instead of JobsScheduler
```

The advanced scheduler demonstrates:

#### 1. Cron Scheduling (`CronExampleJob`)

Runs at specific times using cron expressions. Example: "0 0 9 ? \* MON-FRI" runs every weekday at 9:00 AM.

**Common Cron Expressions:**

- `0 0 * * * ?` - Every day at midnight
- `0 0 9-17 * * MON-FRI` - Every hour (9 AM - 5 PM) on weekdays
- `0 0/30 * * * ?` - Every 30 minutes
- `0 0 0 1 * ?` - First day of every month at midnight

#### 2. Simple Interval Scheduling (`HelloJob`)

Runs repeatedly at fixed intervals. Example: Every 10 seconds.

```csharp
WithSimpleSchedule(x => x
    .WithIntervalInSeconds(10)
    .RepeatForever())
```

#### 3. Job Data Map (`DataMapExampleJob`)

Pass custom parameters to jobs via JobDataMap. Data is persisted in MongoDB and available to the job on each execution.

```csharp
var jobDataMap = new JobDataMap
{
    { "JobName", "Email Notification Job" },
    { "ExecutionCount", 0 },
    { "DepartmentId", 42L }
};

// Access in job:
var jobName = context.JobDetail.JobDataMap.GetString("JobName");
var count = context.JobDetail.JobDataMap.GetIntValue("ExecutionCount");
```

#### 4. Calendar Interval Scheduling (`CalendarIntervalExampleJob`)

Runs at regular calendar-based intervals (days, weeks, months).

```csharp
WithCalendarIntervalSchedule(x => x
    .WithIntervalInWeeks(2))  // Every 2 weeks
```

**Available Methods:**

- `WithIntervalInDays(n)` - Every N days
- `WithIntervalInWeeks(n)` - Every N weeks
- `WithIntervalInMonths(n)` - Every N months

#### 5. Daily Time Interval Scheduling (`DailyTimeIntervalExampleJob`)

Runs multiple times per day during specific hours. Supports day-of-week filtering.

```csharp
WithDailyTimeIntervalSchedule(x => x
    .OnMondayThroughFriday()
    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(9, 0))
    .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0))
    .WithIntervalInHours(1))
```

#### 6. Multiple Triggers on One Job

A single job can be triggered by multiple triggers with different schedules.

```csharp
var triggers = new List<ITrigger> {
    trigger1,  // Every 15 seconds
    trigger2   // Daily at 6 PM
};
await _scheduler.ScheduleJob(job, triggers, replace: true, cancellationToken);
```

## Job Files

- **HelloJob.cs** - Simple job that prints a message
- **CronExampleJob.cs** - Demonstrates cron-based scheduling
- **DataMapExampleJob.cs** - Shows how to use JobDataMap for parameters
- **DailyTimeIntervalExampleJob.cs** - Business hours scheduling example
- **CalendarIntervalExampleJob.cs** - Calendar-based interval example

## Scheduler Files

- **JobsScheduler.cs** - Basic scheduler (default in Program.cs)
- **AdvancedJobsScheduler.cs** - Advanced scheduler with 5 different scheduling patterns

## Key Concepts

### Persistence

All jobs and triggers are persisted in MongoDB. This means:

- Jobs survive scheduler restarts
- Multiple scheduler instances can share the same job store (clustering)
- Job execution history is tracked

### Misfires

If a trigger is supposed to fire but can't (e.g., scheduler is down), the misfire policy determines what happens:

- `IgnoreMisfirePolicy` - No action
- `SimpleTriggerMisfireHandlingInstructionFireNow` - Fire immediately
- `SimpleTriggerMisfireHandlingInstructionRescheduleNextWithRemainingCount` - Reschedule

### Threading

Jobs execute in a thread pool managed by Quartz.NET. Mark jobs as non-concurrent to prevent overlapping executions:

```csharp
[DisallowConcurrentExecution]
public class MyJob : IJob { ... }
```

## Debugging

### View Scheduled Jobs in MongoDB

```javascript
// Connect to MongoDB and view jobs
db.Quartz.JobDetail.find();
db.Quartz.Trigger.find();

// View specific job
db.Quartz.JobDetail.findOne({ "Id.Name": "CronJob" });
```

### Check Scheduler Logs

The scheduler logs important events. Enable debug logging in `appsettings.json`:

```json
"Logging": {
  "LogLevel": {
    "Default": "Information",
    "Quartz": "Debug"
  }
}
```

## Troubleshooting

**Jobs not firing?**

- Check MongoDB connection in appsettings.json
- Ensure MongoDB is running and accessible
- Check logs for lock contention or scheduling errors
- Verify cron expression syntax at [crontab.guru](https://crontab.guru)

**Connection timeout?**

- Increase the connection timeout in MongoDB connection string
- Example: `mongodb://localhost:27017/?serverSelectionTimeoutMS=5000`

**Clustering issues?**

- Ensure all scheduler instances use the same `collectionPrefix`
- Use unique `instanceId` for each instance
- Check network connectivity between instances and MongoDB

## Further Reading

- [Quartz.NET Documentation](https://www.quartz-scheduler.net/)
- [MongoDB Quartz Store Documentation](https://github.com/DhananjayNazare/Quartz.Store.MongoDb)
- [Cron Expression Guide](https://crontab.guru/)
