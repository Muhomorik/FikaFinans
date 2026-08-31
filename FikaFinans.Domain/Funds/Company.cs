namespace FikaFinans.Domain.Funds;

/// <summary>
/// The organisation that runs a fund — fund company, management company, asset
/// manager.
/// </summary>
/// <remarks>
/// The producer's export filenames spell this as a <c>family</c> token holding
/// the lower-cased company name — same value, different word. The lower-casing
/// is a file-naming concern and stays in the paths service.
/// </remarks>
public readonly record struct Company(string Value)
{
    public static Company From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new Company(value);
    }

    public override string ToString() => Value;
}
