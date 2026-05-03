using FikaFinans.InfrastructureV2.Tests.Models.ThesisValidator;

namespace FikaFinans.InfrastructureV2.Tests.Agents.ThesisValidator;

// Result of a single LLM thesis-refinement call. The agent compares the LLM's
// label against the baseline; jumps of more than one step (e.g. Valid → Invalid)
// are rejected and the baseline is kept. We keep verdicts as their own type so
// stub clients in tests can return them without serialization concerns.
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
