using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Fetch;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Fetch;

/// <summary>
/// The CSV-backed metadata provider over the checked-in
/// <c>YieldRaccoon_metadata_schroder_2026-W18.csv</c> fixture — 16 Schroder funds.
/// </summary>
[TestFixture]
[TestOf(typeof(YieldRaccoonCsvBackedMetadataProvider))]
public sealed class YieldRaccoonCsvBackedMetadataProviderTests
{
    private const int FixtureFundCount = 16;
    private static readonly Company Schroder = Company.From("Schroder");
    private static readonly IsoWeek FixtureWeek = IsoWeek.From("2026-W18");

    private IFixture _fixture = null!;
    private YieldRaccoonCsvBackedMetadataProvider _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        _sut = _fixture.Create<YieldRaccoonCsvBackedMetadataProvider>();
    }

    [Test]
    [TestOf(nameof(YieldRaccoonCsvBackedMetadataProvider.ListMetadataAsync))]
    public async Task ListMetadataAsync_ExportPresent_ReturnsEveryRow()
    {
        // Act
        var result = await _sut.ListMetadataAsync(Schroder, FixtureWeek);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(FixtureFundCount));
            Assert.That(result.Select(m => m.Category), Has.None.Empty);
        });
    }

    [Test]
    [TestOf(nameof(YieldRaccoonCsvBackedMetadataProvider.ListMetadataAsync))]
    public async Task ListMetadataAsync_NoExportForWeek_ReturnsEmpty()
    {
        // Act
        var result = await _sut.ListMetadataAsync(Schroder, IsoWeek.From("2020-W01"));

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    [TestOf(nameof(YieldRaccoonCsvBackedMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_KnownIsin_ReturnsItsRow()
    {
        // Act
        var result = await _sut.GetMetadataAsync(new Isin("LU0106252389"), Schroder, FixtureWeek);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Category, Is.EqualTo("Tillväxtmarknader"));
    }

    [Test]
    [TestOf(nameof(YieldRaccoonCsvBackedMetadataProvider.GetMetadataAsync))]
    public async Task GetMetadataAsync_UnknownIsin_ReturnsNull()
    {
        // Act
        var result = await _sut.GetMetadataAsync(new Isin("LU9999999999"), Schroder, FixtureWeek);

        // Assert
        Assert.That(result, Is.Null);
    }
}
