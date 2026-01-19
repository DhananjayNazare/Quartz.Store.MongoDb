using System.Threading.Tasks;
using MongoDB.Driver;
using Quartz.Store.MongoDb.Models;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Repositories
{
    [CollectionName("schedulers")]
    internal class SchedulerRepository : BaseRepository<Scheduler>
    {
        public SchedulerRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task AddScheduler(Scheduler scheduler, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(async () =>
                await Collection.ReplaceOneAsync(sch => sch.Id == scheduler.Id,
                    scheduler, new ReplaceOptions()
                    {
                        IsUpsert = true
                    }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }

        public async Task DeleteScheduler(string id, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.DeleteOneAsync(sch => sch.Id == new SchedulerId(id, InstanceName), cancellationToken)).ConfigureAwait(false);
        }

        public async Task UpdateState(string id, SchedulerState state, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.UpdateOneAsync(sch => sch.Id == new SchedulerId(id, InstanceName),
                UpdateBuilder.Set(sch => sch.State, state), null, cancellationToken)).ConfigureAwait(false);
        }
    }
}