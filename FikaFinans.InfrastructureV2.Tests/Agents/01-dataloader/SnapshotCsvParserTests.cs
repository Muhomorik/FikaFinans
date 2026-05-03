using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class SnapshotCsvParserTests
{
    private const string Header =
        "isin,as_of_date,return_12w_compound_pct,ann_volatility_12w_pct,sharpe_12w,max_drawdown_12w_pct,return_1y_compound_pct,ann_volatility_1y_pct,sharpe_1y,max_drawdown_1y_pct";

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new Fixture().Customize(new AutoMoqCustomization());

    [Test]
    public void Parse_TwoRows_ReturnsDictKeyedByIsin()
    {
        var csv = $"""
            {Header}
            LU0000000001,2026-04-30,12.4645,28.5030,1.8167,-10.7239,55.1817,23.0268,1.5339,-10.7239
            LU0000000002,2026-04-30,4.1312,26.1555,0.7037,-8.9809,36.0889,23.3823,1.0794,-10.5986
            """;
        var sut = _fixture.Create<SnapshotCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "LU0000000001", "LU0000000002" }));
        Assert.That(result["LU0000000001"].Sharpe12w, Is.EqualTo(1.8167m));
        Assert.That(result["LU0000000001"].AsOfDate, Is.EqualTo(new DateOnly(2026, 4, 30)));
    }

    [Test]
    public void Parse_NaNCells_ReturnNull()
    {
        var csv = $"""
            {Header}
            LU0000000001,2026-04-30,NaN,NaN,NaN,NaN,5.0,10.0,0.5,-2.0
            """;
        var sut = _fixture.Create<SnapshotCsvParser>();

        var result = sut.Parse(new StringReader(csv))["LU0000000001"];

        Assert.Multiple(() =>
        {
            Assert.That(result.Return12wCompoundPct, Is.Null);
            Assert.That(result.AnnVolatility12wPct, Is.Null);
            Assert.That(result.Sharpe12w, Is.Null);
            Assert.That(result.MaxDrawdown12wPct, Is.Null);
            Assert.That(result.Return1yCompoundPct, Is.EqualTo(5.0m));
        });
    }

    [Test]
    public void Parse_EmptyCells_ReturnNull()
    {
        var csv = $"""
            {Header}
            LU0000000001,2026-04-30,,,,,5.0,10.0,0.5,-2.0
            """;
        var sut = _fixture.Create<SnapshotCsvParser>();

        var result = sut.Parse(new StringReader(csv))["LU0000000001"];

        Assert.That(result.Return12wCompoundPct, Is.Null);
        Assert.That(result.Sharpe12w, Is.Null);
    }
}
