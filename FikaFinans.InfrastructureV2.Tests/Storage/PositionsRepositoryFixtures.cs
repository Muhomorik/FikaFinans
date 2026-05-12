using FikaFinans.Application.Storage.Bank.Entities;

namespace FikaFinans.InfrastructureV2.Tests.Storage;

/// <summary>
/// Builder helpers for seeding <see cref="InMemoryPositionsRepository"/>
/// from test code. The CSV-loaded path
/// (<see cref="InMemoryPositionsRepository.SeededFromCsv"/>) covers
/// "use the real fixture file"; this builder covers "compose specific
/// positions inline" for focused regression tests.
/// </summary>
/// <remarks>
/// Designed to read fluently at the call site, e.g.:
/// <code>
/// var repo = PositionsRepositoryFixtures.GivenPositions()
///     .Add("LU0000000001", "Foo", currentValueKr: 5_000m, costBasisKr: 4_000m)
///     .Add("LU0000000002", "Bar", currentValueKr: 3_000m, costBasisKr: 2_500m)
///     .WithCash(100_000m)
///     .Build();
/// </code>
/// </remarks>
public sealed class PositionsRepositoryFixtures
{
    private const string PositionsPartition = "positions";
    private const string CashRowKey = "CASH";

    private readonly List<PositionEntity> _rows = new();
    private decimal? _cashKr;

    private PositionsRepositoryFixtures() { }

    public static PositionsRepositoryFixtures GivenPositions() => new();

    public PositionsRepositoryFixtures Add(
        string isin,
        string? name,
        decimal currentValueKr,
        decimal costBasisKr,
        decimal units = 0m,
        decimal avgCostPerUnit = 0m)
    {
        _rows.Add(new PositionEntity
        {
            PartitionKey = PositionsPartition,
            RowKey = isin,
            Isin = isin,
            Name = name,
            CurrentValueKr = currentValueKr,
            CostBasisKr = costBasisKr,
            Units = units,
            AvgCostPerUnit = avgCostPerUnit,
            LastUpdatedAt = DateTimeOffset.UtcNow,
            Source = "testFixture",
        });
        return this;
    }

    public PositionsRepositoryFixtures WithCash(decimal cashKr)
    {
        _cashKr = cashKr;
        return this;
    }

    public InMemoryPositionsRepository Build()
    {
        var repo = new InMemoryPositionsRepository();
        foreach (var row in _rows)
            repo.UpsertAsync(row).GetAwaiter().GetResult();

        if (_cashKr is { } cash)
        {
            repo.UpsertAsync(new PositionEntity
            {
                PartitionKey = PositionsPartition,
                RowKey = CashRowKey,
                Isin = CashRowKey,
                Name = "Cash",
                CurrentValueKr = cash,
                CostBasisKr = cash,
                Units = 0m,
                AvgCostPerUnit = 0m,
                LastUpdatedAt = DateTimeOffset.UtcNow,
                Source = "testFixture",
            }).GetAwaiter().GetResult();
        }

        return repo;
    }

    /// <summary>
    /// Shortcut: seed straight from a positions.csv on disk. Equivalent to
    /// <see cref="InMemoryPositionsRepository.SeededFromCsv"/> — exposed
    /// here so call sites that use <c>PositionsRepositoryFixtures</c> can
    /// stay symmetric.
    /// </summary>
    public static InMemoryPositionsRepository LoadFromCsv(string csvPath)
        => InMemoryPositionsRepository.SeededFromCsv(csvPath);
}
