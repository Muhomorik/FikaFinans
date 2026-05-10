using FikaFinans.Application.Settings;

namespace FikaFinans.Application.Schedules;

/// <summary>Persists schedule configuration to the OS task scheduler.</summary>
public interface IScheduleWriter
{
    void ApplySchedules(ScheduleSettings schedules);
}
