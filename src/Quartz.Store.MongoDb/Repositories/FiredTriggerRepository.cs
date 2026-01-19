using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Quartz.Store.MongoDb.Models;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Repositories
{
    [CollectionName("firedTriggers")]
    internal class FiredTriggerRepository : BaseRepository<FiredTrigger>
    {
        public FiredTriggerRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<List<FiredTrigger>> GetFiredTriggers(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<FiredTrigger>> GetFiredTriggers(string instanceId, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName && trigger.InstanceId == instanceId)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<FiredTrigger>> GetRecoverableFiredTriggers(string instanceId, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(
                    trigger =>
                        trigger.Id.InstanceName == InstanceName && trigger.InstanceId == instanceId &&
                        trigger.RequestsRecovery).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task AddFiredTrigger(FiredTrigger firedTrigger, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.InsertOneAsync(firedTrigger, null, cancellationToken)).ConfigureAwait(false);
        }

        public async Task DeleteFiredTrigger(string firedInstanceId, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.DeleteOneAsync(trigger => trigger.Id == new FiredTriggerId(firedInstanceId, InstanceName), cancellationToken)).ConfigureAwait(false);
        }

        public async Task<long> DeleteFiredTriggersByInstanceId(string instanceId, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await DbRetryHelper.RunWithRetriesAsync(async () =>
                await Collection.DeleteManyAsync(
                    trigger => trigger.Id.InstanceName == InstanceName && trigger.InstanceId == instanceId, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            return result.DeletedCount;
        }

        public async Task UpdateFiredTrigger(FiredTrigger firedTrigger, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.ReplaceOneAsync(trigger => trigger.Id == firedTrigger.Id, firedTrigger, cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        // Backward-compatible overload
        public Task UpdateFiredTrigger(FiredTrigger firedTrigger)
        {
            return UpdateFiredTrigger(firedTrigger, System.Threading.CancellationToken.None);
        }
    }
}