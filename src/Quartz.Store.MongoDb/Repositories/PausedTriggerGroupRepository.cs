using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Quartz.Impl.Matchers;
using Quartz.Store.MongoDb.Extensions;
using Quartz.Store.MongoDb.Models;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Repositories
{
    [CollectionName("pausedTriggerGroups")]
    internal class PausedTriggerGroupRepository : BaseRepository<PausedTriggerGroup>
    {
        public PausedTriggerGroupRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<List<string>> GetPausedTriggerGroups(System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(group => group.Id.InstanceName == InstanceName)
                .Project(group => group.Id.Group)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> IsTriggerGroupPaused(string group, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(g => g.Id == new PausedTriggerGroupId(group, InstanceName)).AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task AddPausedTriggerGroup(string group, System.Threading.CancellationToken cancellationToken = default)
        {
            await Collection.InsertOneAsync(new PausedTriggerGroup()
            {
                Id = new PausedTriggerGroupId(group, InstanceName)
            }, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task DeletePausedTriggerGroup(GroupMatcher<TriggerKey> matcher, System.Threading.CancellationToken cancellationToken = default)
        {
            var regex = matcher.ToBsonRegularExpression().ToRegex();
            await Collection.DeleteManyAsync(group => group.Id.InstanceName == InstanceName && regex.IsMatch(group.Id.Group), cancellationToken).ConfigureAwait(false);
        }

        public async Task DeletePausedTriggerGroup(string groupName, System.Threading.CancellationToken cancellationToken = default)
        {
            await Collection.DeleteOneAsync(group => group.Id == new PausedTriggerGroupId(groupName, InstanceName), cancellationToken).ConfigureAwait(false);
        }
    }
}