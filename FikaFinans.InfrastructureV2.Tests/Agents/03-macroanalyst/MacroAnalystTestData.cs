using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Macro;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

/// <summary>
/// Builders shared by the step 3 agent's test files — a linked set of reports and a fund
/// universe carrying just the categories a test needs.
/// </summary>
internal static class MacroAnalystTestData
{
    public const string Iso = "2026-W17";

    public static WeeklySummaryRun MakeSummary(
        string? runId = null,
        RunStatus status = RunStatus.Success,
        string periodIsoWeek = Iso) =>
        new()
        {
            ReportType    = "weekly-summary",
            RunId         = runId ?? Guid.NewGuid().ToString(),
            Status        = status,
            PeriodIsoWeek = periodIsoWeek,
            NetMood       = MarketSentiment.Mixed,
            MoodSummary   = "Mixed week.",
            Themes        = new List<WeeklySummaryTheme>
            {
                new() {
                    Category   = "Energy",
                    Summary    = "Crude up.",
                    Confidence = ConfidenceLevel.High,
                    Sentiment  = MarketSentiment.RiskOff,
                },
            },
        };

    public static SubstitutionChainRun MakeChain(
        WeeklySummaryRun summary,
        string? runId = null,
        string? periodIsoWeek = null,
        RunStatus status = RunStatus.Success) =>
        new()
        {
            ReportType         = "substitution-chain",
            RunId              = runId ?? Guid.NewGuid().ToString(),
            Status             = status,
            PeriodIsoWeek      = periodIsoWeek ?? summary.PeriodIsoWeek,
            WeeklySummaryRunId = summary.RunId,
            Chains             = new List<RotationChain>
            {
                new() { CapitalFleeing = "Cyclicals", FlowsToward = "Energy", Mechanism = "Oil pressure" },
            },
        };

    public static OpportunityScanRun MakeTargets(
        SubstitutionChainRun chain,
        string? runId = null,
        string? periodIsoWeek = null,
        RunStatus status = RunStatus.Success) =>
        new()
        {
            ReportType             = "rotation-targets",
            RunId                  = runId ?? Guid.NewGuid().ToString(),
            Status                 = status,
            PeriodIsoWeek          = periodIsoWeek ?? chain.PeriodIsoWeek,
            SubstitutionChainRunId = chain.RunId,
            Targets                = new List<RotationTarget>
            {
                new() {
                    Category       = "Energy",
                    SignalStrength = SignalStrength.Moderate,
                    Rationale      = "One chain.",
                    RiskCaveat     = "De-escalation.",
                },
            },
        };

    public static DataLoaderOutput MakeDataLoaderOutput(string isoWeek, IReadOnlyList<string> categories)
    {
        var fund = (string cat, int i) => new FundRecord
        {
            Isin = $"LU{i:D10}",
            Metadata = new FundMetadata
            {
                Isin                     = $"LU{i:D10}",
                Name                     = $"Fund {i}",
                CompanyName              = "Schroder",
                CurrencyCode             = "SEK",
                Category                 = cat,
                FundType                 = "Equity",
                IsIndexFund              = false,
                ManagedType              = "Active",
                TotalFee                 = 1.5m,
                ManagementFee            = 1.0m,
                Risk                     = 5,
                Rating                   = 4,
                SharpeRatioStatic        = 0.5m,
                StandardDeviationStatic  = 0.15m,
                RecommendedHoldingPeriod = "5 years",
                Capital                  = 1_000_000m,
                NumberOfOwners           = 100,
            },
            NavBuckets    = new List<NavBucket>(),
            Snapshot      = null,
            CurrentlyHeld = false,
            Layer         = FundLayer.Active,
        };
        var funds = categories.Select((c, i) => fund(c, i)).ToList();
        return new DataLoaderOutput
        {
            GeneratedAt     = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek         = isoWeek,
            Family          = "schroder",
            RunId           = new PipelineRunId("test-run"),
            ConfigVersion   = "1.0.0",
            Funds           = funds,
            FrozenPositions = new List<FrozenPosition>(),
            CashAvailableKr = 0m,
            DataQuality     = new DataQuality(),
        };
    }
}
