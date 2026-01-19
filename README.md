# Quartz.Store.MongoDb

MongoDB-backed job store implementation for Quartz.NET. Persist your scheduled jobs, triggers and calendar data in MongoDB so multiple scheduler instances can share state and recover after restarts.

<!-- Badges (replace placeholders) -->

[![Build Status](https://img.shields.io/badge/build-Release-blue)](https://github.com/DhananjayNazare/Quartz.Store.MongoDb/actions)
[![NuGet](https://img.shields.io/badge/nuget-Quartz.Store.MongoDb-orange)](https://www.nuget.org/packages/quartz-store-mongodb)
[![License](https://img.shields.io/badge/license-License-green)](https://github.com/DhananjayNazare/Quartz.Store.MongoDb/blob/main/LICENSE)

## Overview

`Quartz.Store.MongoDb` implements a persistent `IJobStore` for Quartz.NET backed by MongoDB. Use it to:

- Persist job and trigger metadata across restarts
- Share scheduler state between instances (clustering)
- Improve reliability for scheduled workloads
- Automatic retry with exponential backoff for transient MongoDB errors
- Atomic distributed locking with TTL-based expiry

## Supported frameworks

- .NET 9

## Key Features

### Reliable MongoDB Integration
- **Transient Error Retries**: Automatic exponential backoff with jitter for connection timeouts and transient MongoDB failures
- **Distributed Locking**: Atomic lock acquisition using MongoDB `FindOneAndUpdate` with TTL-based expiry to prevent deadlocks
- **Cancellation Support**: Full `CancellationToken` propagation through all repository operations and MongoDB driver calls
- **UTC Timestamps**: Consistent use of UTC for all scheduling operations to avoid timezone-related bugs

### High-Performance Scheduling
- Efficient trigger acquisition with MongoDB indexes on `NextFireTime` and `State`
- Batch updates for pause/resume operations using `UpdateMany`
- Minimal round-trips to MongoDB for lock operations

## Installation

NuGet Package Manager:

```powershell
Install-Package quartz-store-mongodb
```

dotnet CLI:

```bash
dotnet add package quartz-store-mongodb
```

Paket:

```bash
paket add quartz-store-mongodb
```

## Quick start (.NET 9)

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

## Legacy example (NameValueCollection)

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

## Configuration options

Common properties (the job store prefix is `quartz.jobStore`):

| Key                                | Type   | Default  | Description                                                                |
| ---------------------------------- | ------ | -------- | -------------------------------------------------------------------------- |
| `quartz.jobStore.connectionString` | string | -        | MongoDB connection string (e.g. `mongodb://user:pass@host:27017/database`) |
| `quartz.jobStore.collectionPrefix` | string | `quartz` | Prefix for collections used to store jobs/triggers                         |
| `quartz.jobStore.useTls`           | bool   | `false`  | Enable TLS/SSL when required by MongoDB                                    |
| `quartz.scheduler.instanceId`      | string | auto     | Scheduler instance id (use stable value for clustering)                    |
| `quartz.jobStore.misfireThreshold` | ms     | 60000    | Misfire threshold in milliseconds (default 1 minute)                       |
| `quartz.jobStore.dbRetryInterval`  | ms     | 15000    | Delay between retries for transient MongoDB errors (default 15 seconds)    |

Store your connection string in environment variables or a secrets manager for production. Example `appsettings.json`:

```json
{
  "Quartz": {
    "quartz.jobStore.connectionString": "mongodb://localhost:27017/quartz",
    "quartz.jobStore.collectionPrefix": "quartz",
    "quartz.scheduler.instanceId": "AUTO",
    "quartz.jobStore.misfireThreshold": "60000",
    "quartz.jobStore.dbRetryInterval": "15000"
  }
}
```

## Performance & Reliability

### Automatic Retry with Exponential Backoff
The job store automatically retries transient MongoDB errors (connection timeouts, network issues) with exponential backoff and jitter:
- Initial backoff: 200ms
- Exponential growth: 2^attempt multiplier
- Max attempts: 3 (configurable)
- Jitter added to reduce thundering herd

This is applied to all write operations (insert, update, delete) on jobs, triggers, calendars, and scheduler state.

### Distributed Locking with TTL
- **Atomic Acquisition**: Uses MongoDB `FindOneAndUpdate` for single-round-trip lock acquisition
- **Automatic Expiry**: Expired locks are automatically cleaned up via MongoDB TTL index (30-second default)
- **Ownership Verification**: Prevents non-owner lock release and handles expired lock re-acquisition
- **Non-Reentrant**: Same thread cannot acquire the same lock twice (will block until first is released)

Lock documents use an `ExpireAt` field for TTL cleanup. If an instance crashes, its locks automatically expire after 30 seconds, allowing other instances to acquire them.

### CancellationToken Support
All async operations support `CancellationToken` for graceful shutdown and timeout control. Cancellation is properly propagated from the scheduler down through job store and repository methods to MongoDB driver calls.

### UTC-Based Scheduling
All timestamps are stored and compared in UTC to avoid timezone-related bugs and misfire detection issues across different regions and locales.

## Troubleshooting

- **Cannot connect to MongoDB**: verify the connection string, network, and authentication. Try `mongo` shell or a MongoDB client.
- **Scheduler state not shared**: ensure all instances use the same `connectionString` and `collectionPrefix` and are pointed at the same database.
- **Duplicate key errors**: caused by collection prefix changes or schema drift; ensure consistent `collectionPrefix` and consider migration scripts.
- **Lock timeout (scheduler hangs)**: if a scheduler instance crashes while holding a lock, other instances can acquire it after ~30 seconds (lock TTL). If hangs persist, check:
  - MongoDB connectivity and network latency
  - Ensure all instances are using UTC time (check system clocks)
  - Check logs for transient MongoDB errors that exceed retry threshold
- **High latency on job acquisition**: verify MongoDB indexes exist:
  - `triggers` collection: index on `(NextFireTime, State, InstanceName)`
  - `triggers` collection: index on `(JobKey, State)`
  - `firedTriggers` collection: TTL index on `CreatedAt` (if using automatic cleanup)

## Collections & Schema

The job store creates the following collections in MongoDB (collection names prefixed by `collectionPrefix`):

| Collection | Purpose | TTL Index |
| --- | --- | --- |
| `jobs` | Stores `JobDetail` models | None |
| `triggers` | Stores trigger configurations and state | None (but indexes on NextFireTime, State) |
| `calendars` | Stores calendar exclusion rules | None |
| `locks` | Distributed lock documents | Yes (30 seconds on ExpireAt) |
| `firedTriggers` | Tracks in-progress and recoverable triggers | Optional (on CreatedAt) |
| `pausedTriggerGroups` | Tracks paused trigger group names | None |
| `schedulers` | Tracks scheduler instance state | None |

Indexes are automatically created on first use by each repository.

## Development

### Running Tests

Tests require a running MongoDB instance (default: `localhost:27017`). Start MongoDB before running tests:

```bash
# Using Docker
docker run -d -p 27017:27017 --name mongodb mongo:latest

# Run tests
dotnet test

# Clean up
docker stop mongodb && docker rm mongodb
```

### Code Structure

- **`src/Quartz.Store.MongoDb/`**: Main library
  - `MongoDbJobStore.cs`: Main `IJobStore` implementation
  - `LockManager.cs`: Distributed locking with MongoDB
  - `Repositories/`: CRUD repositories for each entity type
  - `Models/`: Domain models mirroring Quartz.NET interfaces
  - `Serializers/`: Custom BSON serialization for complex types

- **`tests/Quartz.Store.MongoDb.Tests/`**: Integration and contract tests
- **`examples/Quartz.Store.MongoDb.Examples/`**: Real-world usage examples

## Contributing

Contributions are welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Add tests for new functionality
4. Ensure all tests pass: `dotnet test`
5. Submit a pull request

## Links

- Repository: https://github.com/DhananjayNazare/Quartz.Store.MongoDb
- NuGet: https://www.nuget.org/packages/quartz-store-mongodb

## License

`Quartz.Store.MongoDb` is released under `MIT`. See `LICENSE` for details.
