using System.Text.Json;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Json;

namespace FikaFinans.Wpf.Services;

/// <summary>
/// Phase 8 sub-step 8c: per-step VMs read their <c>Step{N}Json</c> column
/// from the <c>IsinProgress</c> partition instead of the legacy disk JSON.
/// Returns <c>null</c> if no rows match the current <c>RunId</c> or the
/// column is empty for every row, letting the VM fall back to the legacy
/// disk read (preserved until sub-step 8e).
/// </summary>
internal static class IsinProgressOutputLoader
{
    private const string Partition = "isin-progress";

    public static async Task<StepLoadResult?> LoadStepFundsAsync(
        IIsinProgressRepository repo,
        PipelineRunId runId,
        Func<IsinProgressEntity, string?> columnSelector,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(runId.Value)) return null;

        var rows = await repo.QueryPartitionAsync(Partition, ct).ConfigureAwait(false);
        var funds = rows
            .Where(r => string.Equals(r.RunId?.Value, runId.Value, StringComparison.Ordinal))
            .Select(columnSelector)
            .Where(json => !string.IsNullOrEmpty(json))
            .Select(json => JsonSerializer.Deserialize<FundRecord>(json!, JsonOptions.Default))
            .Where(f => f is not null)
            .Cast<FundRecord>()
            .ToList();

        if (funds.Count == 0) return null;

        var jsonOut = JsonSerializer.Serialize(funds, JsonOptions.Default);
        return new StepLoadResult(jsonOut, funds);
    }

    internal sealed record StepLoadResult(string Json, IReadOnlyList<FundRecord> Funds);
}
