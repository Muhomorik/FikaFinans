using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Application.Pipeline.Llm;

public sealed class DifferentiatorLine
{
    public required Isin Isin { get; init; }
    public required string Differentiator { get; init; }
}
