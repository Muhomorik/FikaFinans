using System.Collections.Concurrent;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Application.Storage.Bank.Entities;
using FikaFinans.Infrastructure.Pipeline.Csv;

namespace FikaFinans.InfrastructureV2.Tests.Storage;

/// <summary>
/// In-memory test double for <see cref="IPositionsRepository"/>. Backed by
/// a thread-safe dictionary keyed by <c>(PartitionKey, RowKey)</c> — same
/// shape as Azure Tables / SQLite under the hood.
/// </summary>
/// <remarks>
/// Lives in this test project (per the chunk-7 plan) so production code
/// doesn't ship a memory-backed repo. The CSV-seeded factory below is
/// the bridge that lets cross-stage agent tests construct a
/// <see cref="FikaFinans.Infrastructure.Pipeline.Agents.DataLoaderAgent"/>
/// with the same positions.csv data the runtime path used to read directly.
/// </remarks>
public sealed class InMemoryPositionsRepository : IPositionsRepository
{
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly ConcurrentDictionary<(string Pk, string Rk), PositionEntity> _store = new();

    public Task<PositionEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue((partitionKey, rowKey), out var e) ? e : null);

    public Task<IReadOnlyList<PositionEntity>> QueryPartitionAsync(string partitionKey, CancellationToken ct = default)
    {
        var rows = _store
            .Where(kvp => kvp.Key.Pk == partitionKey)
            .Select(kvp => kvp.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<PositionEntity>>(rows);
    }

    public Task UpsertAsync(PositionEntity entity, CancellationToken ct = default)
    {
        _store[(entity.PartitionKey, entity.RowKey)] = entity;
        return Task.CompletedTask;
    }

    public Task UpsertBatchAsync(string partitionKey, IReadOnlyList<PositionEntity> entities, CancellationToken ct = default)
    {
        foreach (var e in entities)
            _store[(e.PartitionKey, e.RowKey)] = e;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string partitionKey, string rowKey, CancellationToken ct = default)
    {
        _store.TryRemove((partitionKey, rowKey), out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seed the repo from a positions.csv file using the production parser.
    /// Used by cross-stage tests that bootstrap the pipeline through
    /// <c>DataLoaderAgent.Run(...)</c> — replaces the old "agent reads
    /// positions.csv directly" pattern.
    /// </summary>
    public static InMemoryPositionsRepository SeededFromCsv(string csvPath)
    {
        var repo = new InMemoryPositionsRepository();
        if (!File.Exists(csvPath)) return repo;

        using var reader = new StreamReader(csvPath);
        var parsed = new PositionsCsvParser().Parse(reader);
        var now = DateTimeOffset.UtcNow;

        foreach (var p in parsed.Holdings)
        {
            var isin = p.Isin.Value;
            repo._store[(PositionsPartition, isin)] = new PositionEntity
            {
                PartitionKey = PositionsPartition,
                RowKey = isin,
                Isin = isin,
                Name = p.Name,
                CurrentValueKr = p.CurrentValueKr,
                CostBasisKr = p.CostBasisKr,
                Units = 0m,
                AvgCostPerUnit = 0m,
                LastUpdatedAt = now,
                Source = "testSeed",
            };
        }

        repo._store[(PositionsPartition, CashRowKey)] = new PositionEntity
        {
            PartitionKey = PositionsPartition,
            RowKey = CashRowKey,
            Isin = CashRowKey,
            Name = "Cash",
            CurrentValueKr = parsed.CashAvailableKr,
            CostBasisKr = parsed.CashAvailableKr,
            Units = 0m,
            AvgCostPerUnit = 0m,
            LastUpdatedAt = now,
            Source = "testSeed",
        };

        return repo;
    }
}
