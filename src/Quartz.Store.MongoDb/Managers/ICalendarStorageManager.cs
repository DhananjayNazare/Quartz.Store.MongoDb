namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages calendar storage, retrieval, and deletion operations.</summary>
public interface ICalendarStorageManager
{
    /// <summary>Stores a calendar.</summary>
    Task StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken token);

    /// <summary>Retrieves a calendar by name.</summary>
    Task<ICalendar> RetrieveCalendar(string calName, CancellationToken token);

    /// <summary>Removes a calendar by name.</summary>
    Task<bool> RemoveCalendar(string calName, CancellationToken token);

    /// <summary>Checks if a calendar exists.</summary>
    Task<bool> CalendarExists(string calName, CancellationToken token);

    /// <summary>Gets the number of calendars in the store.</summary>
    Task<int> GetNumberOfCalendars(CancellationToken token);

    /// <summary>Gets all calendar names.</summary>
    Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken token);
}
