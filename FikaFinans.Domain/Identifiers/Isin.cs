namespace FikaFinans.Domain.Identifiers;

public readonly record struct Isin
{
    public string Value { get; }

    public Isin(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToUpperInvariant();
    }

    public static implicit operator Isin(string value) => new(value);
    public override string ToString() => Value;
}
