using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.InfrastructureV2.Tests.Models.DataLoader;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class SummaryCsvParserTests
{
    private const string Header =
        "isin,name,period_start,period_end,first_nav,last_nav,nav_high,nav_low,return_2w_pct,ann_volatility_2w_pct,max_drawdown_2w_pct,current_drawdown_pct,sharpe_2w,best_day_pct,worst_day_pct,pct_positive_days,skewness";

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new Fixture().Customize(new AutoMoqCustomization());

    [Test]
    public void Parse_TwoIsinsThreeRows_GroupsByIsin()
    {
        var csv = $"""
            {Header}
            LU0000000001,A,2025-04-01,2025-04-14,100,105,106,99,5.0,10.0,-1.0,-0.5,1.2,2.0,-1.0,55.0,0.1
            LU0000000001,A,2025-04-15,2025-04-28,105,108,110,104,2.8,9.0,-2.0,-1.0,0.9,1.5,-2.0,60.0,0.2
            LU0000000002,B,2025-04-01,2025-04-14,200,210,212,199,5.0,11.0,-1.5,-0.3,1.1,1.8,-1.5,52.0,0.0
            """;
        var sut = _fixture.Create<SummaryCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Is.EquivalentTo(new[] { "LU0000000001", "LU0000000002" }));
            Assert.That(result["LU0000000001"], Has.Count.EqualTo(2));
            Assert.That(result["LU0000000002"], Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Parse_BucketsAreSortedByPeriodEndAscending()
    {
        // Source order intentionally reversed.
        var csv = $"""
            {Header}
            LU0000000001,A,2025-04-15,2025-04-28,105,108,110,104,2.8,9.0,-2.0,-1.0,0.9,1.5,-2.0,60.0,0.2
            LU0000000001,A,2025-04-01,2025-04-14,100,105,106,99,5.0,10.0,-1.0,-0.5,1.2,2.0,-1.0,55.0,0.1
            """;
        var sut = _fixture.Create<SummaryCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        var buckets = result["LU0000000001"];
        Assert.That(buckets[0].PeriodEnd, Is.EqualTo(new DateOnly(2025, 4, 14)));
        Assert.That(buckets[1].PeriodEnd, Is.EqualTo(new DateOnly(2025, 4, 28)));
    }

    [Test]
    public void Parse_NaNInSharpe2w_ReturnsNull()
    {
        var csv = $"""
            {Header}
            LU0000000001,A,2025-04-01,2025-04-14,100,100,100,100,0.0,0.001,0.0,0.0,NaN,0.0,0.0,50.0,0.0
            """;
        var sut = _fixture.Create<SummaryCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result["LU0000000001"][0].Sharpe2w, Is.Null);
    }

    [Test]
    public void Parse_NumericSharpe2w_ParsesAsDecimal()
    {
        var csv = $"""
            {Header}
            LU0000000001,A,2025-04-01,2025-04-14,100,105,106,99,5.0,10.0,-1.0,-0.5,1.2345,2.0,-1.0,55.0,0.1
            """;
        var sut = _fixture.Create<SummaryCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.That(result["LU0000000001"][0].Sharpe2w, Is.EqualTo(1.2345m));
    }

    [Test]
    public void Parse_AllScalarFields_PopulatedCorrectly()
    {
        var csv = $"""
            {Header}
            LU0000000001,A,2025-04-01,2025-04-14,100.5,105.25,106.75,99.0,5.7982,32.1067,-2.2392,-2.1286,3.5562,4.7751,-2.2392,38.4615,1.0573
            """;
        var sut = _fixture.Create<SummaryCsvParser>();

        var b = sut.Parse(new StringReader(csv))["LU0000000001"][0];

        Assert.Multiple(() =>
        {
            Assert.That(b.PeriodStart, Is.EqualTo(new DateOnly(2025, 4, 1)));
            Assert.That(b.FirstNav, Is.EqualTo(100.5m));
            Assert.That(b.LastNav, Is.EqualTo(105.25m));
            Assert.That(b.NavHigh, Is.EqualTo(106.75m));
            Assert.That(b.NavLow, Is.EqualTo(99.0m));
            Assert.That(b.Return2wPct, Is.EqualTo(5.7982m));
            Assert.That(b.AnnVolatility2wPct, Is.EqualTo(32.1067m));
            Assert.That(b.MaxDrawdown2wPct, Is.EqualTo(-2.2392m));
            Assert.That(b.CurrentDrawdownPct, Is.EqualTo(-2.1286m));
            Assert.That(b.Sharpe2w, Is.EqualTo(3.5562m));
            Assert.That(b.BestDayPct, Is.EqualTo(4.7751m));
            Assert.That(b.WorstDayPct, Is.EqualTo(-2.2392m));
            Assert.That(b.PctPositiveDays, Is.EqualTo(38.4615m));
            Assert.That(b.Skewness, Is.EqualTo(1.0573m));
        });
    }
}
