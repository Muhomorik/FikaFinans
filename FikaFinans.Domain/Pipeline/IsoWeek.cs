using System.Globalization;

namespace FikaFinans.Domain.Pipeline;

/// <summary>
/// An ISO-8601 week label in <c>YYYY-Www</c> form (for example <c>2026-W18</c>).
/// </summary>
/// <remarks>
/// <see cref="From"/> carries the value verbatim — no format validation beyond
/// null/whitespace, so callers must not assume a parsed year or week number.
/// <see cref="FromDate"/> always produces a well-formed label.
/// </remarks>
public readonly record struct IsoWeek(string Value)
{
    /// <summary>
    /// No week yet — the placeholder a holder starts with before it knows the date.
    /// </summary>
    public static readonly IsoWeek Empty = new(string.Empty);

    public static IsoWeek From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new IsoWeek(value);
    }

    /// <summary>
    /// Returns the ISO-8601 week a trading date falls in. Every pipeline step stamps its
    /// run through this, so all steps agree on the week of the same run.
    /// </summary>
    /// <param name="date">Read in its own offset; converting to UTC can move the week.</param>
    /// <returns>A label in <c>YYYY-Www</c> form, for example <c>2026-W18</c>.</returns>
    public static IsoWeek FromDate(DateTimeOffset date)
    {
        var day = date.Date;

        return new IsoWeek(string.Create(
            CultureInfo.InvariantCulture,
            $"{ISOWeek.GetYear(day):D4}-W{ISOWeek.GetWeekOfYear(day):D2}"));
    }

    public override string ToString() => Value;
}
