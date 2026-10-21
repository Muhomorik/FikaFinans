using FikaFinans.Application.Schedules;

namespace FikaFinans.Application.Tests.Schedules;

[TestFixture]
[TestOf(typeof(DailyTime))]
public sealed class DailyTimeTests
{
    private static readonly TimeOnly At = new(22, 45);

    // Central European — the zone step 10's cloud timer is set in. Windows and IANA ids both
    // resolve on .NET 9.
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    [Test]
    public void NextAfter_BeforeTodaysTime_IsTodayAtThatTime()
    {
        // Arrange — 18:00 local on a summer day, UTC+2.
        var now = new DateTimeOffset(2026, 6, 5, 18, 0, 0, TimeSpan.FromHours(2));

        // Act
        var next = DailyTime.NextAfter(now, At, Stockholm);

        // Assert
        Assert.That(next, Is.EqualTo(new DateTimeOffset(2026, 6, 5, 22, 45, 0, TimeSpan.FromHours(2))));
    }

    [Test]
    public void NextAfter_AfterTodaysTime_IsTomorrowAtThatTime()
    {
        // Arrange
        var now = new DateTimeOffset(2026, 6, 5, 23, 10, 0, TimeSpan.FromHours(2));

        // Act
        var next = DailyTime.NextAfter(now, At, Stockholm);

        // Assert
        Assert.That(next, Is.EqualTo(new DateTimeOffset(2026, 6, 6, 22, 45, 0, TimeSpan.FromHours(2))));
    }

    [Test]
    public void NextAfter_ExactlyAtTheTime_IsTomorrow()
    {
        // Arrange — the timer has just fired; re-arming must not fire again today.
        var now = new DateTimeOffset(2026, 6, 5, 22, 45, 0, TimeSpan.FromHours(2));

        // Act
        var next = DailyTime.NextAfter(now, At, Stockholm);

        // Assert
        Assert.That(next, Is.EqualTo(new DateTimeOffset(2026, 6, 6, 22, 45, 0, TimeSpan.FromHours(2))));
    }

    [Test]
    public void NextAfter_AcrossTheAutumnClockChange_KeepsTheLocalTime()
    {
        // Arrange — Saturday 24 Oct 2026, summer time; Sunday 25 Oct the clocks go back an hour.
        var now = new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.FromHours(2));

        // Act
        var next = DailyTime.NextAfter(now, At, Stockholm);

        // Assert — 22:45 on the Sunday, now winter time, UTC+1.
        Assert.That(next, Is.EqualTo(new DateTimeOffset(2026, 10, 25, 22, 45, 0, TimeSpan.FromHours(1))));
    }

    [Test]
    public void NextAfter_NowGivenInUtc_ReadsItInTheZone()
    {
        // Arrange — 20:30 UTC is 22:30 in Stockholm in summer: still before 22:45 local.
        var now = new DateTimeOffset(2026, 6, 5, 20, 30, 0, TimeSpan.Zero);

        // Act
        var next = DailyTime.NextAfter(now, At, Stockholm);

        // Assert
        Assert.That(next, Is.EqualTo(new DateTimeOffset(2026, 6, 5, 22, 45, 0, TimeSpan.FromHours(2))));
    }
}
