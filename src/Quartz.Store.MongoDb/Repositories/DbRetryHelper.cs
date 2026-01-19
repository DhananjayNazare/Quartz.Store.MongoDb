using System;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace Quartz.Store.MongoDb.Repositories
{
    internal static class DbRetryHelper
    {
        private static readonly Random Jitterer = new Random();

        public static async Task<T> RunWithRetriesAsync<T>(Func<Task<T>> action, int maxAttempts = 3, TimeSpan? baseDelay = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var attempts = 0;
            baseDelay ??= TimeSpan.FromMilliseconds(200);

            while (true)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (MongoException ex) when (IsTransient(ex))
                {
                    attempts++;
                    if (attempts >= maxAttempts)
                    {
                        throw;
                    }

                    // exponential backoff with jitter
                    var delay = TimeSpan.FromMilliseconds(baseDelay.Value.TotalMilliseconds * Math.Pow(2, attempts - 1));
                    var jitter = TimeSpan.FromMilliseconds(Jitterer.Next(0, (int)Math.Min(1000, delay.TotalMilliseconds)));
                    await Task.Delay(delay + jitter).ConfigureAwait(false);
                }
            }
        }

        public static async Task RunWithRetriesAsync(Func<Task> action, int maxAttempts = 3, TimeSpan? baseDelay = null)
        {
            await RunWithRetriesAsync(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }, maxAttempts, baseDelay).ConfigureAwait(false);
        }

        private static bool IsTransient(MongoException ex)
        {
            return ex is MongoConnectionException || ex is MongoExecutionTimeoutException || ex is MongoCommandException || ex is MongoWriteException && ex.InnerException is TimeoutException;
        }
    }
}
