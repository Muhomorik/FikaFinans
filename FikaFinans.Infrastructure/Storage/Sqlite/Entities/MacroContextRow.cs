using FikaFinans.Application.Storage;

namespace FikaFinans.Infrastructure.Storage.Sqlite.Entities;

/// <summary>
/// EF row for the <c>MacroContexts</c> table — the SQLite-side counterpart of
/// <see cref="FikaFinans.Application.Storage.Bank.Entities.MacroContextEntity"/>.
/// Composite primary key <c>(PartitionKey, RowKey)</c>, inherited from
/// <see cref="TableEntity"/>, mirrors Tables row identity.
/// </summary>
public sealed class MacroContextRow : TableEntity
{
    public string IsoWeek { get; set; } = string.Empty;
    public string GeneratedAt { get; set; } = string.Empty;
    public string ContextJson { get; set; } = string.Empty;
}
