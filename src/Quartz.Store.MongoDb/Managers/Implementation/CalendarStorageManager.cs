using System.Linq;
using Calendar = Quartz.Store.MongoDb.Models.Calendar;

namespace Quartz.Store.MongoDb.Managers;

/// <summary>Manages calendar storage, retrieval, and deletion operations.</summary>
internal class CalendarStorageManager : BaseStorageManager, ICalendarStorageManager
{
    private readonly CalendarRepository _calendarRepository;
    private readonly TriggerRepository _triggerRepository;
    private readonly string _instanceName;

    public CalendarStorageManager(
        CalendarRepository calendarRepository,
        TriggerRepository triggerRepository,
        LockManager lockManager,
        string instanceId,
        string instanceName)
        : base(lockManager, instanceId)
    {
        _calendarRepository = calendarRepository ?? throw new ArgumentNullException(nameof(calendarRepository));
        _triggerRepository = triggerRepository ?? throw new ArgumentNullException(nameof(triggerRepository));
        _instanceName = instanceName ?? throw new ArgumentNullException(nameof(instanceName));
    }

    /// <summary>Stores a calendar.</summary>
    public async Task StoreCalendar(string name, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken token)
    {
        try
        {
            await ExecuteWithLock(async () =>
            {
                await StoreCalendarInternal(name, calendar, replaceExisting, updateTriggers, token);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Retrieves a calendar by name.</summary>
    public async Task<ICalendar> RetrieveCalendar(string calName, CancellationToken token)
    {
        var result = await _calendarRepository.GetCalendar(calName).ConfigureAwait(false);
        return result?.GetCalendar();
    }

    /// <summary>Removes a calendar by name.</summary>
    public async Task<bool> RemoveCalendar(string calName, CancellationToken token)
    {
        try
        {
            return await ExecuteWithLock(async () =>
            {
                return await RemoveCalendarInternal(calName);
            }, token);
        }
        catch (Exception ex)
        {
            throw new JobPersistenceException(ex.Message, ex);
        }
    }

    /// <summary>Checks if a calendar exists.</summary>
    public async Task<bool> CalendarExists(string calName, CancellationToken token)
    {
        return await _calendarRepository.CalendarExists(calName).ConfigureAwait(false);
    }

    /// <summary>Gets the number of calendars in the store.</summary>
    public async Task<int> GetNumberOfCalendars(CancellationToken token)
    {
        return (int)await _calendarRepository.GetCount().ConfigureAwait(false);
    }

    /// <summary>Gets all calendar names.</summary>
    public async Task<IReadOnlyCollection<string>> GetCalendarNames(CancellationToken token)
    {
        var names = await _calendarRepository.GetCalendarNames().ConfigureAwait(false);
        return names.ToList();
    }

    /// <summary>Internal method to store a calendar.</summary>
    private async Task StoreCalendarInternal(string calName, ICalendar calendar, bool replaceExisting, bool updateTriggers, CancellationToken token)
    {
        var existingCal = await CalendarExists(calName, token).ConfigureAwait(false);
        if (existingCal && !replaceExisting)
        {
            throw new ObjectAlreadyExistsException("Calendar with name '" + calName + "' already exists.");
        }

        if (existingCal)
        {
            if (await _calendarRepository.UpdateCalendar(new Calendar(calName, calendar, _instanceName)).ConfigureAwait(false) == 0)
            {
                throw new JobPersistenceException("Couldn't store calendar. Update failed.");
            }

            if (updateTriggers)
            {
                var triggers = await _triggerRepository.GetTriggers(calName).ConfigureAwait(false);
                foreach (var trigger in triggers)
                {
                    var quartzTrigger = (IOperableTrigger)trigger.GetTrigger();
                    quartzTrigger.UpdateWithNewCalendar(calendar, TimeSpan.FromMinutes(1)); // Using default misfire threshold
                    // Note: Full implementation would use actual MisfireThreshold from JobStore
                    await _triggerRepository.UpdateTrigger(trigger).ConfigureAwait(false);
                }
            }
        }
        else
        {
            await _calendarRepository.AddCalendar(new Calendar(calName, calendar, _instanceName)).ConfigureAwait(false);
        }
    }

    /// <summary>Internal method to remove a calendar.</summary>
    private async Task<bool> RemoveCalendarInternal(string calendarName)
    {
        if (await _triggerRepository.TriggersExists(calendarName).ConfigureAwait(false))
        {
            throw new JobPersistenceException("Calendar cannot be removed if it referenced by a trigger!");
        }

        return await _calendarRepository.DeleteCalendar(calendarName).ConfigureAwait(false) > 0;
    }
}
