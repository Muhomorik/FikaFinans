namespace FikaFinans.Domain.Pipeline;

/// <summary>
/// An ISO-8601 week label in <c>YYYY-Www</c> form (for example <c>2026-W18</c>).
/// </summary>
/// <remarks>
/// Carries the value verbatim — no format validation beyond null/whitespace, so
/// callers must not assume a parsed year or week number.
/// </remarks>
public readonly record struct IsoWeek(string Value)
{
    public static IsoWeek From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new IsoWeek(value);
    }

    public override string ToString() => Value;
}
