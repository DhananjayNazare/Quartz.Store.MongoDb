using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using Quartz.Impl.Matchers;
using Quartz.Store.MongoDb.Extensions;
using Quartz.Store.MongoDb.Models;
using Quartz.Store.MongoDb.Models.Id;

namespace Quartz.Store.MongoDb.Repositories
{
    [CollectionName("jobs")]
    internal class JobDetailRepository : BaseRepository<JobDetail>
    {
        public JobDetailRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<JobDetail> GetJob(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(detail => detail.Id == new JobDetailId(jobKey, InstanceName)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<JobKey>> GetJobsKeys(GroupMatcher<JobKey> matcher, System.Threading.CancellationToken cancellationToken = default)
        {
            var list =
                await Collection.Find(FilterBuilder.And(
                    FilterBuilder.Eq(detail => detail.Id.InstanceName, InstanceName),
                    FilterBuilder.Regex(detail => detail.Id.Group, matcher.ToBsonRegularExpression())))
                    .Project(detail => detail.Id)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(id => id.GetJobKey()).ToList();
        }

        public async Task<IEnumerable<string>> GetJobGroupNames(System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection
                .Distinct(detail => detail.Id.Group, detail => detail.Id.InstanceName == InstanceName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task AddJob(JobDetail jobDetail, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.InsertOneAsync(jobDetail, null, cancellationToken)).ConfigureAwait(false);
        }

        public async Task<long> UpdateJob(JobDetail jobDetail, bool upsert, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await DbRetryHelper.RunWithRetriesAsync(async () =>
                await Collection.ReplaceOneAsync(detail => detail.Id == jobDetail.Id,
                    jobDetail,
                    new ReplaceOptions
                    {
                        IsUpsert = upsert
                    }, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task UpdateJobData(JobKey jobKey, JobDataMap jobDataMap, System.Threading.CancellationToken cancellationToken = default)
        {
            await DbRetryHelper.RunWithRetriesAsync(() => Collection.UpdateOneAsync(detail => detail.Id == new JobDetailId(jobKey, InstanceName),
                UpdateBuilder.Set(detail => detail.JobDataMap, jobDataMap), null, cancellationToken)).ConfigureAwait(false);
        }

        public async Task<long> DeleteJob(JobKey key, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await DbRetryHelper.RunWithRetriesAsync(async () =>
                await Collection.DeleteOneAsync(FilterBuilder.Where(job => job.Id == new JobDetailId(key, InstanceName)), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
            return result.DeletedCount;
        }

        public async Task<bool> JobExists(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(detail => detail.Id == new JobDetailId(jobKey, InstanceName)).AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<long> GetCount(System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(detail => detail.Id.InstanceName == InstanceName).CountAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}