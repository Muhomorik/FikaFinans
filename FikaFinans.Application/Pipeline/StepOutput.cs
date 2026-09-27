using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// One run's persisted step output read back for display: the records plus the JSON a
/// frontend renders, so the frontend serializes nothing itself.
/// </summary>
public sealed record StepOutput(string Json, IReadOnlyList<FundRecord> Funds);
