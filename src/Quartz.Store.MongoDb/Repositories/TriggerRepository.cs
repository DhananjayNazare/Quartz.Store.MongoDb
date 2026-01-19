using System;
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
    [CollectionName("triggers")]
    internal class TriggerRepository : BaseRepository<Trigger>
    {
        public TriggerRepository(IMongoDatabase database, string instanceName, string collectionPrefix = null)
            : base(database, instanceName, collectionPrefix)
        {
        }

        public async Task<bool> TriggerExists(TriggerKey key, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(trigger => trigger.Id == new TriggerId(key, InstanceName)).AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> TriggersExists(string calendarName, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(
                    trigger => trigger.Id.InstanceName == InstanceName && trigger.CalendarName == calendarName).AnyAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<Trigger> GetTrigger(TriggerKey key, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(trigger => trigger.Id == new TriggerId(key, InstanceName)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<Models.TriggerState> GetTriggerState(TriggerKey triggerKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(trigger => trigger.Id == new TriggerId(triggerKey, InstanceName))
                .Project(trigger => trigger.State)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<JobDataMap> GetTriggerJobDataMap(TriggerKey triggerKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(trigger => trigger.Id == new TriggerId(triggerKey, InstanceName))
                .Project(trigger => trigger.JobDataMap)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<Trigger>> GetTriggers(string calendarName, System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(FilterBuilder.Where(trigger => trigger.CalendarName == calendarName)).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<Trigger>> GetTriggers(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey).ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, System.Threading.CancellationToken cancellationToken = default)
        {
            var list = await Collection.Find(FilterBuilder.And(
                FilterBuilder.Eq(trigger => trigger.Id.InstanceName, InstanceName),
                FilterBuilder.Regex(trigger => trigger.Id.Group, matcher.ToBsonRegularExpression())))
                .Project(trigger => trigger.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(id => id.GetTriggerKey()).ToList();
        }

        public async Task<List<TriggerKey>> GetTriggerKeys(Models.TriggerState state, System.Threading.CancellationToken cancellationToken = default)
        {
            var list = await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName && trigger.State == state)
                .Project(trigger => trigger.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(id => id.GetTriggerKey()).ToList();
        }

        public async Task<List<string>> GetTriggerGroupNames(System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Distinct(trigger => trigger.Id.Group,
                trigger => trigger.Id.InstanceName == InstanceName)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<string>> GetTriggerGroupNames(GroupMatcher<TriggerKey> matcher, System.Threading.CancellationToken cancellationToken = default)
        {
            var regex = matcher.ToBsonRegularExpression().ToRegex();
            return await Collection.Distinct(trigger => trigger.Id.Group,
                    trigger => trigger.Id.InstanceName == InstanceName && regex.IsMatch(trigger.Id.Group))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<TriggerKey>> GetTriggersToAcquire(DateTimeOffset noLaterThan, DateTimeOffset noEarlierThan,
            int maxCount, System.Threading.CancellationToken cancellationToken = default)
        {
            if (maxCount < 1)
            {
                maxCount = 1;
            }

            var noLaterThanDateTime = noLaterThan.UtcDateTime;
            var noEarlierThanDateTime = noEarlierThan.UtcDateTime;

            var list = await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName &&
                                              trigger.State == Models.TriggerState.Waiting &&
                                              trigger.NextFireTime <= noLaterThanDateTime &&
                                              (trigger.MisfireInstruction == -1 ||
                                               (trigger.MisfireInstruction != -1 &&
                                                trigger.NextFireTime >= noEarlierThanDateTime)))
                .Sort(SortBuilder.Combine(
                    SortBuilder.Ascending(trigger => trigger.NextFireTime),
                    SortBuilder.Descending(trigger => trigger.Priority)
                    ))
                .Limit(maxCount)
                .Project(trigger => trigger.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            return list.Select(id => id.GetTriggerKey()).ToList();

        }

        public async Task<long> GetCount(System.Threading.CancellationToken cancellationToken = default)
        {
            return await Collection.Find(trigger => trigger.Id.InstanceName == InstanceName).CountAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<long> GetCount(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(
                    FilterBuilder.Where(trigger => trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey))
                    .CountAsync(cancellationToken).ConfigureAwait(false);
        }

        [Obsolete]
        public async Task<long> GetMisfireCount(DateTime nextFireTime, System.Threading.CancellationToken cancellationToken = default)
        {
            return
                await Collection.Find(
                    trigger =>
                        trigger.Id.InstanceName == InstanceName &&
                        trigger.MisfireInstruction != MisfireInstruction.IgnoreMisfirePolicy &&
                        trigger.NextFireTime < nextFireTime && trigger.State == Models.TriggerState.Waiting)
                    .CountAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task AddTrigger(Trigger trigger, System.Threading.CancellationToken cancellationToken = default)
        {
            await Collection.InsertOneAsync(trigger, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task UpdateTrigger(Trigger trigger, System.Threading.CancellationToken cancellationToken = default)
        {
            await Collection.ReplaceOneAsync(t => t.Id == trigger.Id, trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        // Backward-compatible overloads without CancellationToken parameter
        public Task<long> UpdateTriggersStates(GroupMatcher<TriggerKey> matcher, Models.TriggerState newState,
            params Models.TriggerState[] oldStates)
        {
            return UpdateTriggersStates(matcher, newState, System.Threading.CancellationToken.None, oldStates);
        }

        public Task<long> UpdateTriggersStates(JobKey jobKey, Models.TriggerState newState,
            params Models.TriggerState[] oldStates)
        {
            return UpdateTriggersStates(jobKey, newState, System.Threading.CancellationToken.None, oldStates);
        }

        public Task<long> UpdateTriggersStates(Models.TriggerState newState, params Models.TriggerState[] oldStates)
        {
            return UpdateTriggersStates(newState, System.Threading.CancellationToken.None, oldStates);
        }

        public async Task<long> UpdateTriggerState(TriggerKey triggerKey, Models.TriggerState state, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await Collection.UpdateOneAsync(trigger => trigger.Id == new TriggerId(triggerKey, InstanceName),
                UpdateBuilder.Set(trigger => trigger.State, state), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateTriggerState(TriggerKey triggerKey, Models.TriggerState newState, Models.TriggerState oldState, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await Collection.UpdateOneAsync(
                trigger => trigger.Id == new TriggerId(triggerKey, InstanceName) && trigger.State == oldState,
                UpdateBuilder.Set(trigger => trigger.State, newState), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateTriggersStates(GroupMatcher<TriggerKey> matcher, Models.TriggerState newState,
            System.Threading.CancellationToken cancellationToken = default, params Models.TriggerState[] oldStates)
        {
            var result = await Collection.UpdateManyAsync(FilterBuilder.And(
                FilterBuilder.Eq(trigger => trigger.Id.InstanceName, InstanceName),
                FilterBuilder.Regex(trigger => trigger.Id.Group, matcher.ToBsonRegularExpression()),
                FilterBuilder.In(trigger => trigger.State, oldStates)),
                UpdateBuilder.Set(trigger => trigger.State, newState), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateTriggersStates(JobKey jobKey, Models.TriggerState newState,
            System.Threading.CancellationToken cancellationToken = default, params Models.TriggerState[] oldStates)
        {
            var result = await Collection.UpdateManyAsync(
                trigger =>
                    trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey &&
                    oldStates.Contains(trigger.State),
                UpdateBuilder.Set(trigger => trigger.State, newState), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateTriggersStates(JobKey jobKey, Models.TriggerState newState, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await Collection.UpdateManyAsync(
                trigger =>
                    trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey,
                UpdateBuilder.Set(trigger => trigger.State, newState), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> UpdateTriggersStates(Models.TriggerState newState, System.Threading.CancellationToken cancellationToken = default, params Models.TriggerState[] oldStates)
        {
            var result = await Collection.UpdateManyAsync(
                trigger =>
                    trigger.Id.InstanceName == InstanceName && oldStates.Contains(trigger.State),
                UpdateBuilder.Set(trigger => trigger.State, newState), null, cancellationToken).ConfigureAwait(false);
            return result.ModifiedCount;
        }

        public async Task<long> DeleteTrigger(TriggerKey key, System.Threading.CancellationToken cancellationToken = default)
        {
            var result =
                await Collection.DeleteOneAsync(FilterBuilder.Where(trigger => trigger.Id == new TriggerId(key, InstanceName)), cancellationToken).ConfigureAwait(false);
            return result.DeletedCount;
        }

        public async Task<long> DeleteTriggers(JobKey jobKey, System.Threading.CancellationToken cancellationToken = default)
        {
            var result = await Collection.DeleteManyAsync(
                FilterBuilder.Where(trigger => trigger.Id.InstanceName == InstanceName && trigger.JobKey == jobKey), cancellationToken).ConfigureAwait(false);
            return result.DeletedCount;
        }

        /// <summary>
        /// Get the names of all of the triggers in the given state that have
        /// misfired - according to the given timestamp.  No more than count will
        /// be returned.
        /// </summary>
        /// <param name="nextFireTime"></param>
        /// <param name="maxResults"></param>
        /// <param name="results"></param>
        /// <returns></returns>
        public bool HasMisfiredTriggers(DateTime nextFireTime, int maxResults, out List<TriggerKey> results)
        {
            var cursor = Collection.Find(
                trigger => trigger.Id.InstanceName == InstanceName &&
                           trigger.MisfireInstruction != MisfireInstruction.IgnoreMisfirePolicy &&
                           trigger.NextFireTime < nextFireTime &&
                           trigger.State == Models.TriggerState.Waiting)
                .Project(trigger => trigger.Id)
                .Sort(SortBuilder.Combine(
                    SortBuilder.Ascending(trigger => trigger.NextFireTime),
                    SortBuilder.Descending(trigger => trigger.Priority)
                    )).ToCursor();

            results = new List<TriggerKey>();

            var hasReachedLimit = false;
            while (cursor.MoveNext() && !hasReachedLimit)
            {
                foreach (var id in cursor.Current)
                {
                    if (results.Count == maxResults)
                    {
                        hasReachedLimit = true;
                    }
                    else
                    {
                        results.Add(id.GetTriggerKey());
                    }
                }
            }
            return hasReachedLimit;
        }
    }
}