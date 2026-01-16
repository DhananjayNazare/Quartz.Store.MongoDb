using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Quartz.Impl;
using Microsoft.Extensions.Configuration;
using Quartz.Store.MongoDb;
using Quartz.Store.MongoDb.Examples;

await Host.CreateDefaultBuilder()
    .ConfigureServices ((cxt, services) =>
    {
        services.AddHostedService<JobsScheduler>();

        services.AddQuartz(q => {

            q.UsePersistentStore<MongoDbJobStore>(options => {
                var section = cxt.Configuration.GetSection("Quartz");
                options.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceName, section.GetValue<string>(StdSchedulerFactory.PropertySchedulerInstanceName));
                options.SetProperty(StdSchedulerFactory.PropertySchedulerInstanceId, section.GetValue<string>(StdSchedulerFactory.PropertySchedulerInstanceId));
                options.SetProperty("quartz.jobStore.connectionString", section.GetValue<string>("quartz.jobStore.connectionString"));
                options.SetProperty("quartz.jobStore.collectionPrefix", section.GetValue<string>("quartz.jobStore.collectionPrefix"));
                options.UseNewtonsoftJsonSerializer();
            });

            services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);
        });

        // Add a small graceful shutdown timeout so the host will wait up to 5 seconds
        // for background services to stop before forcing shutdown.
        services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(5));

    }).Build().RunAsync();
