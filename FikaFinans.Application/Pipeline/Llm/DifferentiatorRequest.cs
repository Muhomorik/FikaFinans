using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Llm;

public sealed class DifferentiatorRequest
{
    public required FundRecord Primary { get; init; }
    public required IReadOnlyList<FundRecord> Alternatives { get; init; }
}
