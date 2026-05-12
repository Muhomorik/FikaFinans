using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using FikaFinans.InfrastructureV2.Tests.Storage;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

/// <summary>
/// Regression tests for the chunk-5 repo seam: confirms
/// <see cref="DataLoaderAgent.ToPositionsParseResult"/> produces the same
/// <c>PositionsParseResult</c> shape that <see cref="PositionsCsvParser"/>
/// did when DataLoader read <c>positions.csv</c> directly. If the adapter
/// or the seeding helper ever drifts, the parser-vs-adapter equivalence
/// here goes red before any downstream agent test does.
/// </summary>
[TestFixture]
public class DataLoaderAgentRepoSeamTests
{
    [Test]
    public void ToPositionsParseResult_RepoSeededFromFixtureCsv_MatchesDirectParserShape()
    {
        using var reader = new StreamReader(Paths.PositionsCsvAbs);
        var directParse = new PositionsCsvParser().Parse(reader);

        var repo = InMemoryPositionsRepository.SeededFromCsv(Paths.PositionsCsvAbs);
        var rows = repo.QueryPartitionAsync("positions").GetAwaiter().GetResult();
        var viaAdapter = DataLoaderAgent.ToPositionsParseResult(rows);

        var directByIsin = directParse.Holdings.ToDictionary(p => p.Isin.Value);
        var adapterByIsin = viaAdapter.Holdings.ToDictionary(p => p.Isin.Value);

        Assert.Multiple(() =>
        {
            Assert.That(viaAdapter.CashAvailableKr, Is.EqualTo(directParse.CashAvailableKr));
            Assert.That(viaAdapter.TotalRowCount, Is.EqualTo(directParse.TotalRowCount));
            Assert.That(viaAdapter.Holdings, Has.Count.EqualTo(directParse.Holdings.Count));
            Assert.That(adapterByIsin.Keys, Is.EquivalentTo(directByIsin.Keys));

            foreach (var (isin, parserPos) in directByIsin)
            {
                var adapterPos = adapterByIsin[isin];
                Assert.That(adapterPos.Name, Is.EqualTo(parserPos.Name), $"name drift on {isin}");
                Assert.That(adapterPos.CurrentValueKr, Is.EqualTo(parserPos.CurrentValueKr), $"current_value_kr drift on {isin}");
                Assert.That(adapterPos.CostBasisKr, Is.EqualTo(parserPos.CostBasisKr), $"cost_basis_kr drift on {isin}");
            }
        });
    }

    [Test]
    public void ToPositionsParseResult_InlineBuilderWithHoldingsAndCash_PreservesShape()
    {
        var repo = PositionsRepositoryFixtures.GivenPositions()
            .Add("LU0000000001", "Foo", currentValueKr: 5_000m, costBasisKr: 4_000m)
            .Add("LU0000000002", "Bar", currentValueKr: 3_000m, costBasisKr: 2_500m)
            .WithCash(100_000m)
            .Build();

        var rows = repo.QueryPartitionAsync("positions").GetAwaiter().GetResult();
        var result = DataLoaderAgent.ToPositionsParseResult(rows);

        Assert.Multiple(() =>
        {
            Assert.That(result.CashAvailableKr, Is.EqualTo(100_000m));
            // Holdings + cash row.
            Assert.That(result.TotalRowCount, Is.EqualTo(3));
            Assert.That(result.Holdings, Has.Count.EqualTo(2));
            Assert.That(result.Warnings, Is.Empty);

            var foo = result.Holdings.Single(h => h.Isin.Value == "LU0000000001");
            var bar = result.Holdings.Single(h => h.Isin.Value == "LU0000000002");
            Assert.That(foo.Name, Is.EqualTo("Foo"));
            Assert.That(foo.CurrentValueKr, Is.EqualTo(5_000m));
            Assert.That(foo.CostBasisKr, Is.EqualTo(4_000m));
            Assert.That(bar.Name, Is.EqualTo("Bar"));
            Assert.That(bar.CurrentValueKr, Is.EqualTo(3_000m));
            Assert.That(bar.CostBasisKr, Is.EqualTo(2_500m));
        });
    }

    [Test]
    public void ToPositionsParseResult_NoCashRow_WarnsAndDefaultsCashToZero()
    {
        var repo = PositionsRepositoryFixtures.GivenPositions()
            .Add("LU0000000001", "Foo", currentValueKr: 5_000m, costBasisKr: 4_000m)
            .Build();

        var rows = repo.QueryPartitionAsync("positions").GetAwaiter().GetResult();
        var result = DataLoaderAgent.ToPositionsParseResult(rows);

        Assert.Multiple(() =>
        {
            Assert.That(result.CashAvailableKr, Is.EqualTo(0m));
            Assert.That(result.Holdings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("CASH"));
        });
    }
}
