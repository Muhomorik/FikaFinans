using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.Pipeline.Run;

namespace FikaFinans.InfrastructureV2.Tests.Pipeline.Run;

[TestFixture]
[TestOf(typeof(PipelineRunIdFactory))]
public sealed class PipelineRunIdFactoryTests
{
    private static readonly DateTimeOffset June5 = new(2026, 6, 5, 18, 30, 0, TimeSpan.Zero);

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp() => _fixture = new Fixture().Customize(new AutoMoqCustomization());

    private PipelineRunIdFactory CreateSut() => _fixture.Create<PipelineRunIdFactory>();

    [Test]
    public void NewRunId_Fund_IsDateIsinAndSuffix()
    {
        // Act
        var runId = CreateSut().NewRunId(new Isin("SE0001234567"), June5);

        // Assert
        Assert.That(runId.Value, Does.Match("^20260605-SE0001234567-[0-9a-f]{8}$"));
    }

    [Test]
    public void NewUniverseRunId_Pass_IsDateUniverseAndSuffix()
    {
        // Act
        var runId = CreateSut().NewUniverseRunId(June5);

        // Assert
        Assert.That(runId.Value, Does.Match("^20260605-universe-[0-9a-f]{8}$"));
    }

    [Test]
    public void NewUniverseRunId_TwoPassesSameDay_AreDistinct()
    {
        // Arrange — two triggers can run the pass on one day; "run again and overwrite".
        var sut = CreateSut();

        // Act
        var first = sut.NewUniverseRunId(June5);
        var second = sut.NewUniverseRunId(June5);

        // Assert
        Assert.That(first, Is.Not.EqualTo(second));
    }
}
