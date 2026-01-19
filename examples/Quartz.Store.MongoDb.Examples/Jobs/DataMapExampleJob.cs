using System;
using System.Threading.Tasks;
using Quartz;

namespace Quartz.Store.MongoDb.Examples.Jobs
{
    /// <summary>
    /// Example job that demonstrates how to use JobDataMap to pass data to jobs.
    /// The data is persisted in MongoDB and available to the job on each execution.
    /// </summary>
    internal class DataMapExampleJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var dataMap = context.JobDetail.JobDataMap;
            
            // Retrieve data from JobDataMap
            var jobName = dataMap.GetString("JobName") ?? "Unknown Job";
            var executionCount = dataMap.GetIntValue("ExecutionCount");
            var departmentId = dataMap.GetLong("DepartmentId");

            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {jobName}");
            Console.WriteLine($"  Job Key: {context.JobDetail.Key}");
            Console.WriteLine($"  Execution Count: {executionCount}");
            Console.WriteLine($"  Department ID: {departmentId}");
            
            // Update execution count for next run
            dataMap["ExecutionCount"] = executionCount + 1;

            await Task.CompletedTask;
        }
    }
}
