namespace FikaFinans.Application.Storage.Bank.Entities;

/// <summary>
/// Tables-shaped row for one week's step 3 macro context. PartitionKey is the constant
/// <c>"macro-contexts"</c>; RowKey is the weekly summary's <c>runId</c> it was built from —
/// the head of the reports' run id chain, so a republished week is a new row.
/// </summary>
/// <remarks>
/// Per week, not per fund, so it does not ride on the progress row; it outlives every run.
/// The ISO week sits beside the key for the lookup and the earlier-week fallback. In cloud
/// it is a partition of the backend's one Azure table, beside the per-ISIN rows.
/// </remarks>
public sealed class MacroContextEntity : TableEntity
{
    /// <summary>The context's ISO week, <c>YYYY-Www</c> — sorts in week order.</summary>
    public string IsoWeek { get; init; } = string.Empty;

    /// <summary>
    /// The context's own <c>generatedAt</c>, round-trip ISO 8601 — orders two contexts of
    /// the same week, newest wins.
    /// </summary>
    public string GeneratedAt { get; init; } = string.Empty;

    /// <summary>The whole <c>MacroContext</c>, in the step output files' JSON shape.</summary>
    public string ContextJson { get; init; } = string.Empty;
}
