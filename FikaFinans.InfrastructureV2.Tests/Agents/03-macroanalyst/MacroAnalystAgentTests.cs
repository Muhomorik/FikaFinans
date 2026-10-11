using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Domain.Macro;
using static FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst.MacroAnalystTestData;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

/// <summary>
/// The agent's pure helpers — validation, slugs, categories. <c>RunInMemoryAsync</c> has its
/// own file, <see cref="MacroAnalystAgentRunInMemoryTests"/>.
/// </summary>
[TestFixture]
[TestOf(typeof(MacroAnalystAgent))]
public sealed class MacroAnalystAgentTests
{
    #region Validation

    [Test]
    public void ValidateInputs_StatusFailed_Throws()
    {
        // Arrange
        var summary = MakeSummary(status: RunStatus.Failed);
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("Failed"));
    }

    [Test]
    public void ValidateInputs_StatusPartial_AddsWarning()
    {
        // Arrange
        var summary = MakeSummary(status: RunStatus.Partial);
        var chain = MakeChain(summary);
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);
        var warnings = new List<string>();

        // Act
        MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, warnings);

        // Assert
        Assert.That(warnings, Has.Some.Contains("Partial"));
    }

    [Test]
    public void ValidateInputs_PeriodMismatchAcrossAnalytics_Throws()
    {
        // Arrange
        var summary = MakeSummary(periodIsoWeek: "2026-W16");
        var chain = MakeChain(summary, periodIsoWeek: "2026-W17");
        var targets = MakeTargets(chain, periodIsoWeek: "2026-W17");
        var dl = MakeDataLoaderOutput("2026-W16", ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("mismatched periodIsoWeek"));
    }

    [Test]
    public void ValidateInputs_WeeklyToChainFkBroken_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chainOrig = MakeChain(summary);
        var chain = new SubstitutionChainRun
        {
            ReportType         = chainOrig.ReportType,
            RunId              = chainOrig.RunId,
            Status             = chainOrig.Status,
            PeriodIsoWeek      = chainOrig.PeriodIsoWeek,
            WeeklySummaryRunId = "00000000-0000-0000-0000-000000000000",
            Chains             = chainOrig.Chains,
        };
        var targets = MakeTargets(chain);
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("FK chain broken"));
    }

    [Test]
    public void ValidateInputs_ChainToTargetsFkBroken_Throws()
    {
        // Arrange
        var summary = MakeSummary();
        var chain = MakeChain(summary);
        var targetsOrig = MakeTargets(chain);
        var targets = new OpportunityScanRun
        {
            ReportType             = targetsOrig.ReportType,
            RunId                  = targetsOrig.RunId,
            Status                 = targetsOrig.Status,
            PeriodIsoWeek          = targetsOrig.PeriodIsoWeek,
            SubstitutionChainRunId = "00000000-0000-0000-0000-000000000000",
            Targets                = targetsOrig.Targets,
        };
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("FK chain broken"));
    }

    [Test]
    public void ValidateInputs_BundleDriftVsDataLoader_Throws()
    {
        // Arrange
        var summary = MakeSummary(periodIsoWeek: "2026-W17");
        var chain = MakeChain(summary, periodIsoWeek: "2026-W17");
        var targets = MakeTargets(chain, periodIsoWeek: "2026-W17");
        var dl = MakeDataLoaderOutput("2026-W18", ["Branschfond, Energi"]);

        // Act
        var ex = Assert.Throws<MacroAnalystValidationException>(
            () => MacroAnalystAgent.ValidateInputs(summary, chain, targets, dl, new List<string>()));

        // Assert
        Assert.That(ex!.Message, Does.Contain("bundle drift"));
    }

    #endregion

    #region Slugify

    [Test]
    public void Slugify_MixedPunctuation_LowercasesAndJoinsWithUnderscores()
    {
        // Arrange
        var label = "Capital flows into AI/Semis!";

        // Act
        var slug = MacroAnalystAgent.Slugify(label);

        // Assert
        Assert.That(slug, Is.EqualTo("capital_flows_into_ai_semis"));
    }

    [Test]
    public void Slugify_Empty_ReturnsUntitled()
    {
        // Arrange
        var label = "";

        // Act
        var slug = MacroAnalystAgent.Slugify(label);

        // Assert
        Assert.That(slug, Is.EqualTo("untitled"));
    }

    #endregion

    #region ExtractCategories

    [Test]
    public void ExtractCategories_DistinctSorted_ReturnsUnique()
    {
        // Arrange
        var dl = MakeDataLoaderOutput(Iso, ["Branschfond, Energi", "Globalfond", "Branschfond, Energi"]);

        // Act
        var cats = MacroAnalystAgent.ExtractCategories(dl);

        // Assert
        Assert.That(cats, Is.EquivalentTo(new[] { "Branschfond, Energi", "Globalfond" }));
    }

    #endregion
}
