using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Agents;
using Moq;
using static FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst.MacroAnalystTestData;
using DataLoaderJsonOptions = FikaFinans.Infrastructure.Pipeline.Json.JsonOptions;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

/// <summary>
/// <see cref="MacroAnalystAgent.RunInMemoryAsync"/> — the in-memory call the signal-driven
/// handler makes. Everything arrives as a parameter, prompts included; nothing is read from
/// <see cref="IPathsService"/> or the prompts provider.
/// </summary>
[TestFixture]
[TestOf(typeof(MacroAnalystAgent))]
public sealed class MacroAnalystAgentRunInMemoryTests
{
    private static readonly IsoWeek Week = IsoWeek.From(Iso);

    // Recognisable markers, so a test can tell which template reached the LLM.
    private static readonly Step03MacroAnalystPrompts Prompts = new(
        SystemPrompt: "SYSTEM-MARKER",
        UserPromptTemplate: "USER-MARKER {iso_week}\n{category_list}",
        RetryPromptTemplate: "RETRY-MARKER {error_message}");

    private const string EmptyResponse = """
        {
          "macro_regime": "Mixed",
          "regime_confidence": 0.4,
          "catalysts": [],
          "rotation_themes": []
        }
        """;

    private IFixture _fixture = null!;
    private Mock<IMacroLlmClient> _llmMock = null!;
    private Mock<IPathsService> _pathsMock = null!;
    private Mock<IStep03MacroAnalystPromptsProvider> _promptsProviderMock = null!;
    private MacroAnalystAgent _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _pathsMock = _fixture.Freeze<Mock<IPathsService>>();
        _promptsProviderMock = _fixture.Freeze<Mock<IStep03MacroAnalystPromptsProvider>>();
        _llmMock = _fixture.Freeze<Mock<IMacroLlmClient>>();
        _sut = _fixture.Create<MacroAnalystAgent>();
    }

    #region In memory

    [Test]
    public async Task RunInMemoryAsync_AnyInput_ReadsNeitherPathsNorPromptsProvider()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm(EmptyResponse);

        // Act
        await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        _pathsMock.VerifyNoOtherCalls();
        _promptsProviderMock.VerifyNoOtherCalls();
    }

    #endregion

    #region Prompts parameter

    [Test]
    public async Task RunInMemoryAsync_Prompts_SendsSystemPromptAndFilledUserTemplate()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm(EmptyResponse);

        // Act
        await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        _llmMock.Verify(x => x.CompleteAsync(
            "SYSTEM-MARKER",
            It.Is<string>(u => u.StartsWith($"USER-MARKER {Iso}") && u.Contains("- Branschfond, Energi")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task RunInMemoryAsync_InvalidFirstResponse_AppendsRetryTemplateWithTheError()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        _llmMock.SetupSequence(x =>
                x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{ not json")
            .ReturnsAsync(EmptyResponse);

        // Act
        await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        _llmMock.Verify(x => x.CompleteAsync(
            "SYSTEM-MARKER",
            It.Is<string>(u => u.Contains("RETRY-MARKER ") && !u.Contains("{error_message}")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Happy path

    [Test]
    public async Task RunInMemoryAsync_HappyPath_ProducesExpectedContext()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi", "Globalfond"]);
        SetupLlm("""
        {
          "macro_regime": "Mixed",
          "macro_regime_secondary": null,
          "regime_confidence": 0.62,
          "catalysts": [
            {
              "name": "Hormuz tensions",
              "intensity": "high",
              "weeks_active": 3,
              "affected_categories": ["Branschfond, Energi"],
              "rationale": "Crude rallying on geopolitical risk."
            }
          ],
          "rotation_themes": [
            {
              "label": "Capital flows into Energy",
              "signal_strength": "Moderate",
              "affected_categories": ["Branschfond, Energi"],
              "rationale": "One chain points there.",
              "source_chain": {
                "capital_fleeing": "Cyclicals",
                "flows_toward": "Energy"
              }
            }
          ]
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(ctx.IsoWeek, Is.EqualTo(Iso));
            Assert.That(ctx.MacroRegime, Is.EqualTo(MacroRegime.Mixed));
            Assert.That(ctx.RegimeConfidence, Is.EqualTo(0.62m));
            Assert.That(ctx.NetMoodInput, Is.EqualTo(MarketSentiment.Mixed));
            Assert.That(ctx.SourceRunIds.WeeklySummaryRunId, Is.EqualTo(summary.RunId));
            Assert.That(ctx.SourceRunIds.SubstitutionChainRunId, Is.EqualTo(chain.RunId));
            Assert.That(ctx.SourceRunIds.RotationTargetsRunId, Is.EqualTo(targets.RunId));
            Assert.That(ctx.Catalysts, Has.Count.EqualTo(1));
            Assert.That(ctx.Catalysts[0].Intensity, Is.EqualTo(Intensity.High));
            Assert.That(ctx.RotationThemes, Has.Count.EqualTo(1));
            Assert.That(ctx.RotationThemes[0].Id, Is.EqualTo($"rot_theme_capital_flows_into_energy_{Iso}"));
            Assert.That(ctx.RotationThemes[0].SignalStrength, Is.EqualTo(SignalStrength.Moderate));
        });
        _llmMock.Verify(
            x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Universe filtering

    [Test]
    public async Task RunInMemoryAsync_HallucinatedCategory_FilteredOut()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        {
          "macro_regime": "RiskOff",
          "regime_confidence": 0.5,
          "catalysts": [
            {
              "name": "Made-up event",
              "intensity": "low",
              "weeks_active": 1,
              "affected_categories": ["AI Hardware", "NotARealCategory"],
              "rationale": "..."
            }
          ],
          "rotation_themes": []
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        Assert.That(ctx.Catalysts, Is.Empty);
        Assert.That(ctx.Warnings, Is.Not.Null);
        Assert.That(ctx.Warnings!, Has.Some.Contains("dropping catalyst"));
    }

    #endregion

    #region Retry behavior

    [Test]
    public async Task RunInMemoryAsync_RegimeConfidenceOutOfRange_RetriesAndUsesValid()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        var bad = """
        {
          "macro_regime": "Mixed",
          "regime_confidence": 1.7,
          "catalysts": [],
          "rotation_themes": []
        }
        """;
        _llmMock.SetupSequence(x =>
                x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bad)
            .ReturnsAsync(EmptyResponse);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        Assert.That(ctx.RegimeConfidence, Is.EqualTo(0.4m));
        _llmMock.Verify(
            x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Test]
    public void RunInMemoryAsync_RetryAlsoFails_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("{ this is not json at all");

        // Act + Assert
        Assert.ThrowsAsync<MacroAnalystValidationException>(
            async () => await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts));
    }

    [Test]
    public async Task RunInMemoryAsync_ExtractsJsonFromMarkdownFence()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        Here is the analysis:
        ```json
        {
          "macro_regime": "Stagflation",
          "regime_confidence": 0.55,
          "catalysts": [],
          "rotation_themes": []
        }
        ```
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);

        // Assert
        Assert.That(ctx.MacroRegime, Is.EqualTo(MacroRegime.Stagflation));
    }

    #endregion

    #region Round-trip serialization

    [Test]
    public async Task RunInMemoryAsync_RoundTripsThroughJsonOptions()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        SetupLlm("""
        {
          "macro_regime": "RiskOn",
          "regime_confidence": 0.8,
          "catalysts": [{
            "name": "Earnings tailwind",
            "intensity": "medium",
            "weeks_active": 2,
            "affected_categories": ["Branschfond, Energi"],
            "rationale": "Beats."
          }],
          "rotation_themes": []
        }
        """);

        // Act
        var ctx = await _sut.RunInMemoryAsync(summary, chain, targets, dl, Week, Prompts);
        var json = JsonSerializer.Serialize(ctx, DataLoaderJsonOptions.Default);
        var roundTripped = JsonSerializer.Deserialize<MacroContext>(json, DataLoaderJsonOptions.Default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"macro_regime\": \"RiskOn\""));
            Assert.That(json, Does.Contain("\"net_mood_input\": \"Mixed\""));
            Assert.That(json, Does.Contain("\"intensity\": \"medium\""));
            Assert.That(roundTripped, Is.Not.Null);
            Assert.That(roundTripped!.MacroRegime, Is.EqualTo(MacroRegime.RiskOn));
            Assert.That(roundTripped.Catalysts[0].Intensity, Is.EqualTo(Intensity.Medium));
        });
    }

    #endregion

    private void SetupLlm(string llmResponse) =>
        _llmMock.Setup(x => x.CompleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(llmResponse);
}
