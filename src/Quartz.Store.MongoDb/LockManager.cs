using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Common.Logging;
using MongoDB.Driver;
using Quartz.Store.MongoDb.Models;
using Quartz.Store.MongoDb.Repositories;

namespace Quartz.Store.MongoDb
{
    /// <summary>
    /// Implements a simple distributed lock on top of MongoDB. It is not a reentrant lock so you can't
    /// acquire the lock more than once in the same thread of execution.
    /// </summary>
    internal class LockManager : IDisposable
    {
        private static readonly TimeSpan SleepThreshold = TimeSpan.FromMilliseconds(1000);

        private static readonly ILog Log = LogManager.GetLogger<LockManager>();

        private readonly LockRepository _lockRepository;

        private readonly ConcurrentDictionary<LockType, LockInstance> _pendingLocks =
            new ConcurrentDictionary<LockType, LockInstance>();

        private readonly SemaphoreSlim _pendingLocksSemaphore = new SemaphoreSlim(1);

        private bool _disposed;

        public LockManager(IMongoDatabase database, string instanceName, string collectionPrefix)
        {
            _lockRepository = new LockRepository(database, instanceName, collectionPrefix);
        }

        public void Dispose()
        {
            EnsureObjectNotDisposed();

            _disposed = true;
            var locks = _pendingLocks.ToArray();
            foreach (var keyValuePair in locks)
            {
                keyValuePair.Value.Dispose();
            }
        }

        public async Task<IDisposable> AcquireLock(LockType lockType, string instanceId)
        {
            while (true)
            {
                EnsureObjectNotDisposed();

                await _pendingLocksSemaphore.WaitAsync();
                try
                {
                    if (await _lockRepository.TryAcquireLock(lockType, instanceId).ConfigureAwait(false))
                    {
                        var lockInstance = new LockInstance(this, lockType, instanceId);
                        AddLock(lockInstance);

                        return lockInstance;
                    }
                }
                finally
                {
                    _pendingLocksSemaphore.Release();
                }

                await Task.Delay(SleepThreshold);
            }
        }
        
        private async Task ReleaseLock(LockInstance lockInstance)
        {
            await _pendingLocksSemaphore.WaitAsync();
            try
            {
                await _lockRepository.ReleaseLock(lockInstance.LockType, lockInstance.InstanceId, CancellationToken.None).ConfigureAwait(false);
                LockReleased(lockInstance);
            }
            finally
            {
                _pendingLocksSemaphore.Release();
            }
        }

        private void EnsureObjectNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LockManager));
            }
        }

        private void AddLock(LockInstance lockInstance)
        {
            if (!_pendingLocks.TryAdd(lockInstance.LockType, lockInstance))
            {
                throw new Exception($"Unable to add lock instance for lock {lockInstance.LockType} on {lockInstance.InstanceId}");
            }
        }

        private void LockReleased(LockInstance lockInstance)
        {
            if (!_pendingLocks.TryRemove(lockInstance.LockType, out _))
            {
                Log.Warn($"Unable to remove pending lock {lockInstance.LockType} on {lockInstance.InstanceId}");
            }
        }

        private class LockInstance : IDisposable
        {
            private readonly LockManager _lockManager;

            private bool _disposed;

            public LockInstance(LockManager lockManager, LockType lockType, string instanceId)
            {
                _lockManager = lockManager;
                LockType = lockType;
                InstanceId = instanceId;
            }

            public string InstanceId { get; }

            public LockType LockType { get; }

            public async ValueTask DisposeAsync()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LockInstance),
                        $"This lock {LockType} for {InstanceId} has already been disposed");
                }

                await _lockManager.ReleaseLock(this).ConfigureAwait(false);
                _disposed = true;
            }

            public void Dispose()
            {
                // For synchronous Dispose (e.g., in finally blocks), we need to signal without blocking
                // The lock will be released asynchronously; this just marks it for cleanup
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(LockInstance),
                        $"This lock {LockType} for {InstanceId} has already been disposed");
                }

                _disposed = true;
                // Fire and forget the async release to avoid blocking
                // The lock will eventually be released or expire via TTL
                _ = _lockManager.ReleaseLock(this);
            }
        }
    }
}