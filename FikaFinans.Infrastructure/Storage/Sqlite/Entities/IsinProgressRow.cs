namespace FikaFinans.Infrastructure.Storage.Sqlite.Entities;

/// <summary>
/// EF row for the <c>IsinProgress</c> table — the SQLite-side counterpart
/// of <see cref="FikaFinans.Application.Storage.Bank.Entities.IsinProgressEntity"/>.
/// Composite primary key <c>(PartitionKey, RowKey)</c> mirrors Tables row
/// identity. State is persisted as <c>string</c> (Tables wire format);
/// repo converts to/from the enum at the entity boundary.
/// </summary>
public sealed class IsinProgressRow
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;

    public string Isin { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public DateTimeOffset? NavDate { get; set; }
    public int CurrentStep { get; set; }
    public DateTimeOffset? LatestProcessedNavDate { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public string? LastError { get; set; }
    public int AttemptCount { get; set; }

    public string? Step01Json { get; set; }
    public string? Step02Json { get; set; }
    public string? Step03Json { get; set; }
    public string? Step04Json { get; set; }
    public string? Step05Json { get; set; }
    public string? Step06Json { get; set; }
    public string? Step07Json { get; set; }
    public string? Step08Json { get; set; }
    public string? Step09Json { get; set; }
}
