using MongoDB.Bson.Serialization.Attributes;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Models
{
    internal class PausedTriggerGroup
    {
        [BsonId]
        public PausedTriggerGroupId Id { get; set; }
    }
}