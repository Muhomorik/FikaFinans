using FikaFinans.Application.Storage;

namespace FikaFinans.Infrastructure.Storage.Sqlite;

/// <summary>
/// Tables-semantic assertions reused by every SQLite repository's batch
/// upsert path. Mirrors Azure Tables' batch limits so behaviour matches
/// once we swap providers in Phase 6.
/// </summary>
internal static class TableBatchAsserts
{
    /// <summary>Maximum rows per Tables batch transaction.</summary>
    public const int MaxBatchSize = 100;

    /// <summary>
    /// Asserts a batch is non-null, within size limit, and every row
    /// belongs to <paramref name="partitionKey"/>. Throws
    /// <see cref="InvalidOperationException"/> on violation.
    /// </summary>
    public static void EnsureSinglePartitionBatch<T>(
        string partitionKey,
        IReadOnlyList<T> entities)
        where T : TableEntity
    {
        ArgumentNullException.ThrowIfNull(entities);

        if (entities.Count > MaxBatchSize)
            throw new InvalidOperationException(
                $"Tables batch limit is {MaxBatchSize} rows; got {entities.Count}.");

        for (var i = 0; i < entities.Count; i++)
        {
            var pk = entities[i].PartitionKey;
            if (!string.Equals(pk, partitionKey, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Batch row {i} has PartitionKey '{pk}' but expected '{partitionKey}'.");
        }
    }
}
