using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Models
{
    internal enum SchedulerState
    {
        Started,
        Running,
        Paused,
        Resumed
    }

    internal class Scheduler
    {
        [BsonId]
        public SchedulerId Id { get; set; }

        [BsonRepresentation(BsonType.String)]
        public SchedulerState State { get; set; }

        public DateTime? LastCheckIn { get; set; }
    }
}