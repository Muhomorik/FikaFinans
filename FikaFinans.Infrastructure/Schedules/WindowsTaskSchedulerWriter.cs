using System.Diagnostics;
using FikaFinans.Application.Schedules;
using FikaFinans.Application.Settings;
using NLog;

namespace FikaFinans.Infrastructure.Schedules;

/// <summary>
/// Creates or removes Windows Task Scheduler entries under <c>\FikaFinans\</c>
/// when the user toggles schedule settings. Shells out to <c>schtasks.exe</c>
/// to avoid an extra NuGet dependency.
/// </summary>
public sealed class WindowsTaskSchedulerWriter : IScheduleWriter
{
    private const string TaskFolder = @"\FikaFinans\";
    private const string DailyRunTask  = @"\FikaFinans\DailyRun";
    private const string WeeklyExportTask = @"\FikaFinans\WeeklyExport";

    private readonly ILogger _logger;

    public WindowsTaskSchedulerWriter(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ApplySchedules(ScheduleSettings schedules)
    {
        ApplyDailyAutoRun(schedules.DailyAutoRun);
        ApplyWeeklyExport(schedules.WeeklyExport);
    }

    private void ApplyDailyAutoRun(DailyAutoRunSettings s)
    {
        if (!s.Enabled)
        {
            DeleteTask(DailyRunTask);
            return;
        }

        var exePath = GetExePath();
        var tr = s.PassAutoList
            ? $"\"{exePath}\" --auto-list"
            : $"\"{exePath}\"";

        RunSchtasks($"/Create /F /TN \"{DailyRunTask}\" /SC DAILY /ST {s.Time} /TR {tr}");
        _logger.Info("Task Scheduler: created {Task} at {Time}", DailyRunTask, s.Time);
    }

    private void ApplyWeeklyExport(WeeklyExportSettings s)
    {
        if (!s.Enabled)
        {
            DeleteTask(WeeklyExportTask);
            return;
        }

        var exePath = GetExePath();
        var dayAbbr = ToSchtasksDay(s.DayOfWeek);
        RunSchtasks($"/Create /F /TN \"{WeeklyExportTask}\" /SC WEEKLY /D {dayAbbr} /ST {s.Time} /TR \"{exePath}\" --export");
        _logger.Info("Task Scheduler: created {Task} on {Day} at {Time}", WeeklyExportTask, dayAbbr, s.Time);
    }

    private void DeleteTask(string taskName)
    {
        // Exit code 1 means task not found — treat as success.
        RunSchtasks($"/Delete /F /TN \"{taskName}\"", ignoreExitCode: true);
        _logger.Info("Task Scheduler: removed {Task} (if it existed)", taskName);
    }

    private void RunSchtasks(string args, bool ignoreExitCode = false)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            _logger.Error("Failed to start schtasks.exe");
            return;
        }

        process.WaitForExit(10_000);

        if (!ignoreExitCode && process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd().Trim();
            _logger.Warn("schtasks.exe exited {Code}: {Error}", process.ExitCode, stderr);
        }
    }

    private static string GetExePath() =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? System.Reflection.Assembly.GetEntryAssembly()?.Location
        ?? throw new InvalidOperationException("Cannot determine the current executable path.");

    private static string ToSchtasksDay(string dayName) => dayName.ToUpperInvariant() switch
    {
        "MONDAY"    => "MON",
        "TUESDAY"   => "TUE",
        "WEDNESDAY" => "WED",
        "THURSDAY"  => "THU",
        "FRIDAY"    => "FRI",
        "SATURDAY"  => "SAT",
        "SUNDAY"    => "SUN",
        _           => "MON",
    };
}
