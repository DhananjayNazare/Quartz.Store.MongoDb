using Common.Logging;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Base class for all storage managers providing common utilities.</summary>
internal abstract class BaseStorageManager
{
    protected readonly LockManager LockManager;
    protected readonly string InstanceId;
    protected readonly ILog Log;

    protected BaseStorageManager(LockManager lockManager, string instanceId)
    {
        LockManager = lockManager ?? throw new ArgumentNullException(nameof(lockManager));
        InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        Log = LogManager.GetLogger(GetType());
    }

    /// <summary>Executes an operation with distributed lock and error handling.</summary>
    protected async Task<T> ExecuteWithLock<T>(Func<Task<T>> operation, CancellationToken token)
    {
        try
        {
            using (await LockManager.AcquireLock(LockType.TriggerAccess, InstanceId).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Error executing operation with lock: {ex.Message}", ex);
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Executes an operation with distributed lock and error handling (void return).</summary>
    protected async Task ExecuteWithLock(Func<Task> operation, CancellationToken token)
    {
        try
        {
            using (await LockManager.AcquireLock(LockType.TriggerAccess, InstanceId).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                await operation().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"Error executing operation with lock: {ex.Message}", ex);
            throw new JobPersistenceException(ex.Message, ex);
        }
    }
}
