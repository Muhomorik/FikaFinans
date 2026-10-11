using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Domain.Tests.Pipeline;

[TestFixture]
[TestOf(typeof(IsoWeek))]
public sealed class IsoWeekTests
{
    // ── FromDate ──────────────────────────────────────────────────────

    [TestCase(2026, 4, 30, "2026-W18")]
    [TestCase(2026, 1, 5, "2026-W02")]
    [TestCase(2026, 12, 31, "2026-W53")]
    [TestCase(2027, 1, 1, "2026-W53")]  // belongs to the previous ISO year
    [TestCase(2024, 12, 30, "2025-W01")] // belongs to the next ISO year
    public void FromDate_TradingDate_ReturnsIsoWeekLabel(int year, int month, int day, string expected)
    {
        // Arrange
        var date = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);

        // Act
        var result = IsoWeek.FromDate(date);

        // Assert
        Assert.That(result.Value, Is.EqualTo(expected));
    }

    [Test]
    public void FromDate_PositiveOffsetJustAfterMidnight_UsesLocalDateNotUtc()
    {
        // Arrange — Monday 00:30 at +02:00 is still Sunday in UTC, the previous week.
        var date = new DateTimeOffset(2026, 5, 4, 0, 30, 0, TimeSpan.FromHours(2));

        // Act
        var result = IsoWeek.FromDate(date);

        // Assert
        Assert.That(result.Value, Is.EqualTo("2026-W19"));
    }

    // ── Empty ─────────────────────────────────────────────────────────

    [Test]
    public void Empty_Always_HasEmptyValue()
    {
        // Act
        var result = IsoWeek.Empty;

        // Assert
        Assert.That(result.Value, Is.Empty);
    }

    // ── From ──────────────────────────────────────────────────────────

    [TestCase("")]
    [TestCase("   ")]
    public void From_Blank_ThrowsArgumentException(string value)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => IsoWeek.From(value));
    }
}
