namespace FikaFinans.Domain.Macro;

public sealed class MatchedTheme
{
    public string? Id { get; init; }
    public string? Label { get; init; }
    public required MatchMethod MatchMethod { get; init; }

    public static MatchedTheme None { get; } = new() { MatchMethod = MatchMethod.None };
}
