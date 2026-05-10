using FikaFinans.Application.Paths;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Domain.Funds;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class PositionsCsvParserTests
{
    private const string Header = "isin,name,cost_basis_kr,current_value_kr";

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
    }

    [Test]
    public void Parse_HoldingsAndCashRow_CarvesOutCash()
    {
        var csv = $"""
            {Header}
            LU0000000001,Foo Fund,8000,10000
            ,Cash,50000,50000
            LU0000000002,Bar Fund,4000,5000
            """;
        var sut = _fixture.Create<PositionsCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.Multiple(() =>
        {
            Assert.That(result.Holdings, Has.Count.EqualTo(2));
            Assert.That(result.Holdings.Select(h => h.Isin.Value), Is.EquivalentTo(new[] { "LU0000000001", "LU0000000002" }));
            Assert.That(result.CashAvailableKr, Is.EqualTo(50000m));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public void Parse_OnlyCashRow_ReturnsCashAndNoHoldings()
    {
        var csv = $"""
            {Header}
            ,Cash,100000,100000
            """;
        var sut = _fixture.Create<PositionsCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.Multiple(() =>
        {
            Assert.That(result.Holdings, Is.Empty);
            Assert.That(result.CashAvailableKr, Is.EqualTo(100000m));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public void Parse_HeaderOnly_NoHoldingsCashZeroWithWarning()
    {
        var csv = $"""
            {Header}
            """;
        var sut = _fixture.Create<PositionsCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.Multiple(() =>
        {
            Assert.That(result.Holdings, Is.Empty);
            Assert.That(result.CashAvailableKr, Is.EqualTo(0m));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("Cash"));
        });
    }

    [Test]
    public void Parse_NoCashRowButHoldingsPresent_WarnsAndReturnsZeroCash()
    {
        var csv = $"""
            {Header}
            LU0000000001,Foo Fund,8000,10000
            """;
        var sut = _fixture.Create<PositionsCsvParser>();

        var result = sut.Parse(new StringReader(csv));

        Assert.Multiple(() =>
        {
            Assert.That(result.Holdings, Has.Count.EqualTo(1));
            Assert.That(result.CashAvailableKr, Is.EqualTo(0m));
            Assert.That(result.Warnings, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Parse_MultipleCashRows_ThrowsHaltException()
    {
        var csv = $"""
            {Header}
            ,Cash,50000,50000
            ,Cash,30000,30000
            """;
        var sut = _fixture.Create<PositionsCsvParser>();

        var ex = Assert.Throws<DataLoaderHaltException>(() => sut.Parse(new StringReader(csv)));
        Assert.That(ex!.Trigger, Is.EqualTo("multiple_cash_rows"));
    }
}
