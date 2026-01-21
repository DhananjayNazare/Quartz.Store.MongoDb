namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages trigger storage, retrieval, and deletion operations.</summary>
public interface ITriggerStorageManager
{
    /// <summary>Stores a trigger.</summary>
    Task StoreTrigger(IOperableTrigger newTrigger, bool replaceExisting, CancellationToken token);

    /// <summary>Retrieves a trigger by its key.</summary>
    Task<IOperableTrigger> RetrieveTrigger(TriggerKey triggerKey, CancellationToken token);

    /// <summary>Removes a trigger by its key.</summary>
    Task<bool> RemoveTrigger(TriggerKey triggerKey, CancellationToken token);

    /// <summary>Removes multiple triggers by their keys.</summary>
    Task<bool> RemoveTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken token);

    /// <summary>Replaces an existing trigger with a new one.</summary>
    Task<bool> ReplaceTrigger(TriggerKey triggerKey, IOperableTrigger newTrigger, CancellationToken token);

    /// <summary>Checks if a trigger exists.</summary>
    Task<bool> CheckExists(TriggerKey triggerKey, CancellationToken token);

    /// <summary>Gets the number of triggers in the store.</summary>
    Task<int> GetNumberOfTriggers(CancellationToken token);

    /// <summary>Gets all trigger keys matching the given group matcher.</summary>
    Task<IReadOnlyCollection<TriggerKey>> GetTriggerKeys(GroupMatcher<TriggerKey> matcher, CancellationToken token);

    /// <summary>Gets all trigger group names.</summary>
    Task<IReadOnlyCollection<string>> GetTriggerGroupNames(CancellationToken token);

    /// <summary>Gets the state of a trigger.</summary>
    Task<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken token);
}
