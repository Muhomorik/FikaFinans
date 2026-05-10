using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Llm;

public sealed class ThesisRefinementVerdict
{
    public required ThesisValidity Validity { get; init; }
    public required string Rationale { get; init; }

    public static ThesisRefinementVerdict ConfirmBaseline(ThesisValidity baseline, string rationale) => new()
    {
        Validity  = baseline,
        Rationale = rationale,
    };
}
