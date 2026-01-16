# Quartz.Store.MongoDb

MongoDB-backed job store implementation for Quartz.NET. Persist your scheduled jobs, triggers and calendar data in MongoDB so multiple scheduler instances can share state and recover after restarts.

<!-- Badges (replace placeholders) -->
[![Build Status](https://img.shields.io/badge/build-PLACEHOLDER-blue)](REPOSITORY_URL/actions)
[![NuGet](https://img.shields.io/badge/nuget-Quartz.Store.MongoDb-orange)](https://www.nuget.org/packages/Quartz.Store.MongoDb)
[![License](https://img.shields.io/badge/license-LICENSE_NAME-green)](REPOSITORY_URL/blob/main/LICENSE)
[![Coverage](https://img.shields.io/badge/coverage-PLACEHOLDER-yellow)](REPOSITORY_URL/coverage)

Overview
--------
`Quartz.Store.MongoDb` implements a persistent `IJobStore` for Quartz.NET backed by MongoDB. Use it to:

- Persist job and trigger metadata across restarts
- Share scheduler state between instances (clustering)
- Improve reliability for scheduled workloads

Supported frameworks
--------------------
- .NET 9

Installation
------------
NuGet Package Manager:

```powershell
Install-Package Quartz.Store.MongoDb
```

dotnet CLI:

```bash
dotnet add package Quartz.Store.MongoDb
```

Paket:

```bash
paket add Quartz.Store.MongoDb
```

Quick start (.NET 9)
---------------------
This example shows how to configure Quartz to use the MongoDB job store with generic host. It reads connection configuration from the `appsettings.json` or an environment variable.

```csharp
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using Quartz.Impl;
using Quartz.Spi.MongoDbJobStore;

await Host.CreateDefaultBuilder()
    .ConfigureServices((cxt, services) =>
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
    })
    .Build()
    .RunAsync();
```

If you prefer environment variables, set `MONGODB_CONNECTION` and read it in place of the configuration section.

Legacy example (NameValueCollection)
-----------------------------------
If your app uses the `StdSchedulerFactory` configuration approach (older frameworks), you can configure the store via `NameValueCollection`:

```csharp
var properties = new NameValueCollection();
properties[StdSchedulerFactory.PropertySchedulerInstanceName] = "DefaultScheduler";
properties[StdSchedulerFactory.PropertySchedulerInstanceId] = $"{Environment.MachineName}-{Guid.NewGuid()}";
properties[StdSchedulerFactory.PropertyJobStoreType] = typeof(Quartz.Spi.MongoDbJobStore.MongoDbJobStore).AssemblyQualifiedName;
properties[$"{StdSchedulerFactory.PropertyJobStorePrefix}.{StdSchedulerFactory.PropertyDataSourceConnectionString}"] = "mongodb://localhost:27017/quartz";
properties[$"{StdSchedulerFactory.PropertyJobStorePrefix}.collectionPrefix"] = "quartz";

var scheduler = new StdSchedulerFactory(properties).GetScheduler().Result;
```

Configuration options
---------------------
Common properties (the job store prefix is `quartz.jobStore`):

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `quartz.jobStore.connectionString` | string | - | MongoDB connection string (e.g. `mongodb://user:pass@host:27017/database`) |
| `quartz.jobStore.collectionPrefix` | string | `quartz` | Prefix for collections used to store jobs/triggers |
| `quartz.jobStore.useTls` | bool | `false` | Enable TLS/SSL when required by MongoDB |
| `quartz.scheduler.instanceId` | string | auto | Scheduler instance id (use stable value for clustering) |

Store your connection string in environment variables or a secrets manager for production. Example `appsettings.json`:

```json
{
  "Quartz": {
    "quartz.jobStore.connectionString": "mongodb://localhost:27017/quartz",
    "quartz.jobStore.collectionPrefix": "quartz",
    "quartz.scheduler.instanceId": "AUTO"
  }
}
```

Troubleshooting
---------------
- Cannot connect to MongoDB: verify the connection string, network, and authentication. Try `mongo` shell or a MongoDB client.
- Scheduler state not shared: ensure all instances use the same `connectionString` and `collectionPrefix` and are pointed at the same database.
- Duplicate key errors: caused by collection prefix changes or schema drift; ensure consistent `collectionPrefix` and consider migration scripts.


Links
-----
- Repository: REPOSITORY_URL
- NuGet: https://www.nuget.org/packages/Quartz.Store.MongoDb
- Documentation: REPOSITORY_URL/wiki or REPOSITORY_URL/docs
- Changelog: REPOSITORY_URL/blob/main/CHANGELOG.md
- Issues: REPOSITORY_URL/issues


License
-------
`Quartz.Store.MongoDb` is released under `MIT`. See `LICENSE` for details.

