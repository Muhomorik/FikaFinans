using FikaFinans.Application.Paths;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Domain.Funds;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class PortfolioStructureMdParserTests
{
    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
    }

    [Test]
    public void Parse_NameOnlyCoreAndWriteoffPinnings_PreservesNameAndNullsIsin()
    {
        const string md = """
            # Portfolio Structure

            ## Pinned funds

            | ISIN | Fund                                  | Layer    | Note                                  |
            | ---- | ------------------------------------- | -------- | ------------------------------------- |
            |      | Storebrand Global All Countries A SEK | core     | Global index anchor — monthly savings |
            |      | Storebrand Global Solutions A SEK     | core     | Global index anchor — monthly savings |
            |      | Swedbank Robur Rysslandsfond A        | writeoff | Frozen — cannot trade                 |
            """;
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Pinnings, Has.Count.EqualTo(3));
            Assert.That(result.Pinnings.All(p => p.Isin is null), Is.True);
            Assert.That(result.Pinnings[0].Name, Is.EqualTo("Storebrand Global All Countries A SEK"));
            Assert.That(result.Pinnings[0].Layer, Is.EqualTo(PinnedLayer.Core));
            Assert.That(result.Pinnings[2].Layer, Is.EqualTo(PinnedLayer.Writeoff));
        });
    }

    [Test]
    public void Parse_IsinAndNamePresent_PopulatesBoth()
    {
        const string md = """
            | ISIN         | Fund      | Layer    | Note    |
            | ------------ | --------- | -------- | ------- |
            | LU0000000001 | Foo Fund  | core     | anchor  |
            | LU0000000002 | Bar Fund  | writeoff | frozen  |
            """;
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Pinnings[0].Isin?.Value, Is.EqualTo("LU0000000001"));
            Assert.That(result.Pinnings[0].Name, Is.EqualTo("Foo Fund"));
            Assert.That(result.Pinnings[0].Layer, Is.EqualTo(PinnedLayer.Core));
            Assert.That(result.Pinnings[1].Isin?.Value, Is.EqualTo("LU0000000002"));
            Assert.That(result.Pinnings[1].Layer, Is.EqualTo(PinnedLayer.Writeoff));
        });
    }

    [Test]
    public void Parse_HeaderRowAndSeparator_AreNotParsedAsData()
    {
        const string md = """
            | ISIN | Fund | Layer | Note |
            | ---- | ---- | ----- | ---- |
            """;
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(md));

        Assert.That(result.Pinnings, Is.Empty);
    }

    [Test]
    public void Parse_BlankLinesAndProse_AreSkipped()
    {
        const string md = """

            Some prose text.

            More text without any pipe.

            | ISIN | Fund     | Layer | Note   |
            | ---- | -------- | ----- | ------ |
            |      | Foo Fund | core  | anchor |
            """;
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(md));

        Assert.That(result.Pinnings, Has.Count.EqualTo(1));
        Assert.That(result.Pinnings[0].Name, Is.EqualTo("Foo Fund"));
    }

    [Test]
    public void Parse_UnknownLayerValue_IsIgnored()
    {
        const string md = """
            | ISIN | Fund     | Layer  | Note   |
            | ---- | -------- | ------ | ------ |
            |      | Foo Fund | active | active |
            |      | Bar Fund | core   | anchor |
            """;
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(md));

        Assert.That(result.Pinnings, Has.Count.EqualTo(1));
        Assert.That(result.Pinnings[0].Name, Is.EqualTo("Bar Fund"));
    }

    [Test]
    public void Parse_EmptyInput_ReturnsEmptyPinnings()
    {
        var sut = _fixture.Create<PortfolioStructureMdParser>();

        var result = sut.Parse(new StringReader(""));

        Assert.That(result.Pinnings, Is.Empty);
    }
}
