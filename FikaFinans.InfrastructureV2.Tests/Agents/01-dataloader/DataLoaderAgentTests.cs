using FikaFinans.Application.Paths;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Csv;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.InfrastructureV2.Tests.Storage;
using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoMoq;
using FikaFinans.Domain.Funds;

namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

[TestFixture]
public class DataLoaderAgentTests
{
    private const string MetadataHeader =
        "isin,name,company_name,currency_code,category,fund_type,is_index_fund,managed_type,total_fee,management_fee,risk,rating,sharpe_ratio,standard_deviation,recommended_holding_period,capital,number_of_owners";
    private const string SummaryHeader =
        "isin,name,period_start,period_end,first_nav,last_nav,nav_high,nav_low,return_2w_pct,ann_volatility_2w_pct,max_drawdown_2w_pct,current_drawdown_pct,sharpe_2w,best_day_pct,worst_day_pct,pct_positive_days,skewness";
    private const string SnapshotHeader =
        "isin,as_of_date,return_12w_compound_pct,ann_volatility_12w_pct,sharpe_12w,max_drawdown_12w_pct,return_1y_compound_pct,ann_volatility_1y_pct,sharpe_1y,max_drawdown_1y_pct";
    private const string PositionsHeader = "isin,name,cost_basis_kr,current_value_kr";

    private IFixture _fixture = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoMoqCustomization());
        _fixture.Inject<IPathsService>(new TestPathsService());
        // Seed from the same positions.csv the runtime path used to read directly,
        // so the happy-path Run() test sees the original cash row + zero holdings.
        _fixture.Inject<IPositionsRepository>(
            InMemoryPositionsRepository.SeededFromCsv(Paths.PositionsCsvAbs));
    }

    private static PositionsParseResult ParsePos(string csv) =>
        new PositionsCsvParser().Parse(new StringReader(csv));

    // ─── #1. Happy path: real fixtures via Paths.cs ─────────────────────────
    [Test]
    public void Run_HappyPathRealFixtures_ProducesJsonFileAndExpectedShape()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        const string runId = "test-happypath";

        var result = sut.Run("schroder", "2026-W18", runId);

        var outPath = Paths.DataLoaderOutput("2026-W18", runId);
        Assert.That(File.Exists(outPath), Is.True, $"Expected output file at {outPath}");

        // Round-trip check.
        var roundTripped = JsonSerializer.Deserialize<DataLoaderOutput>(
            File.ReadAllText(outPath), JsonOptions.Default);
        Assert.That(roundTripped, Is.Not.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Family, Is.EqualTo("schroder"));
            Assert.That(result.IsoWeek, Is.EqualTo("2026-W18"));
            Assert.That(result.RunId, Is.EqualTo(runId));
            Assert.That(result.DataQuality.MetadataRows, Is.EqualTo(16));
            Assert.That(result.Funds, Has.Count.EqualTo(16));
            Assert.That(result.Funds.All(f => f.NavBuckets.Count > 0), Is.True);
            Assert.That(result.Funds.All(f => f.Snapshot != null), Is.True);
            // The real positions.csv only has the Cash row.
            Assert.That(result.CashAvailableKr, Is.EqualTo(100000m));
            Assert.That(result.Funds.Count(f => f.CurrentlyHeld), Is.EqualTo(0));
            // The real portfolio_structure.md pins a Swedbank fund to writeoff,
            // which isn't in the Schroder family — so no frozen_positions and a warning.
            Assert.That(result.FrozenPositions, Is.Empty);
        });
    }

    // ─── #2. Cash-only positions ─────────────────────────────────────────────
    [Test]
    public void RunInMemory_CashOnlyPositions_AllFundsNotHeldAndCashSet()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-cashonly",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo")),
            new StringReader(SummaryHeader + "\n" + RowSummary("LU0000000001", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")),
            ParsePos(PositionsHeader + "\n,Cash,100000,100000"),
            new StringReader(""));

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Has.Count.EqualTo(1));
            Assert.That(result.Funds[0].CurrentlyHeld, Is.False);
            Assert.That(result.CashAvailableKr, Is.EqualTo(100000m));
            Assert.That(result.FrozenPositions, Is.Empty);
        });
    }

    // ─── #3. Missing snapshot row ────────────────────────────────────────────
    [Test]
    public void RunInMemory_FundMissingFromSnapshot_RecordHasNullSnapshotAndWarning()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-missing-snap",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo") + "\n" + RowMeta("LU0000000002", "Bar")),
            new StringReader(SummaryHeader + "\n" + RowSummary("LU0000000001", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")), // LU0000000002 missing
            ParsePos(PositionsHeader + "\n,Cash,0,0"),
            new StringReader(""));

        var bar = result.Funds.Single(f => f.Isin == "LU0000000002");
        Assert.That(bar.Snapshot, Is.Null);
        Assert.That(result.DataQuality.Warnings.Any(w => w.Contains("LU0000000002")), Is.True);
    }

    // ─── #4. Filename {iso_week} mismatch ────────────────────────────────────
    [Test]
    public void VerifyFilenameIsoWeekTags_OneFileMismatches_ThrowsHaltException()
    {
        var ex = Assert.Throws<DataLoaderHaltException>(() =>
            DataLoaderAgent.VerifyFilenameIsoWeekTags(
                "2026-W18",
                "/tmp/YieldRaccoon_metadata_schroder_2026-W18.csv",
                "/tmp/YieldRaccoon_summary_schroder_2026-W17.csv",
                "/tmp/YieldRaccoon_snapshot_schroder_2026-W18.csv"));
        Assert.That(ex!.Trigger, Is.EqualTo("filename_iso_week_mismatch"));
    }

    // ─── #5. Empty positions (header only) ───────────────────────────────────
    [Test]
    public void RunInMemory_EmptyPositions_NoErrorCashZero()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-empty-pos",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo")),
            new StringReader(SummaryHeader + "\n" + RowSummary("LU0000000001", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")),
            ParsePos(PositionsHeader),
            new StringReader(""));

        Assert.Multiple(() =>
        {
            Assert.That(result.CashAvailableKr, Is.EqualTo(0m));
            Assert.That(result.Funds[0].CurrentlyHeld, Is.False);
            Assert.That(result.FrozenPositions, Is.Empty);
        });
    }

    // ─── #6. Core pinning ────────────────────────────────────────────────────
    [Test]
    public void RunInMemory_CorePinning_AssignsCoreLayerToMatchingFunds()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        const string md = """
            | ISIN | Fund | Layer | Note   |
            | ---- | ---- | ----- | ------ |
            |      | Foo  | core  | anchor |
            |      | Bar  | core  | anchor |
            """;
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-core",
            new StringReader(MetadataHeader + "\n" +
                             RowMeta("LU0000000001", "Foo") + "\n" +
                             RowMeta("LU0000000002", "Bar") + "\n" +
                             RowMeta("LU0000000003", "Baz")),
            new StringReader(SummaryHeader + "\n" +
                             RowSummary("LU0000000001", "2025-04-01", "2025-04-14") + "\n" +
                             RowSummary("LU0000000002", "2025-04-01", "2025-04-14") + "\n" +
                             RowSummary("LU0000000003", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" +
                             RowSnap("LU0000000001") + "\n" +
                             RowSnap("LU0000000002") + "\n" +
                             RowSnap("LU0000000003")),
            ParsePos(PositionsHeader + "\n,Cash,0,0"),
            new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Single(f => f.Isin == "LU0000000001").Layer, Is.EqualTo(FundLayer.Core));
            Assert.That(result.Funds.Single(f => f.Isin == "LU0000000002").Layer, Is.EqualTo(FundLayer.Core));
            Assert.That(result.Funds.Single(f => f.Isin == "LU0000000003").Layer, Is.EqualTo(FundLayer.Active));
            Assert.That(result.DataQuality.CoreCount, Is.EqualTo(2));
        });
    }

    // ─── #7. Writeoff pinning + currently held ───────────────────────────────
    [Test]
    public void RunInMemory_WriteoffPinningAndHeld_FrozenPositionsCarriesValueAndFundExcluded()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        const string md = """
            | ISIN | Fund | Layer    | Note   |
            | ---- | ---- | -------- | ------ |
            |      | Bar  | writeoff | frozen |
            """;
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-writeoff-held",
            new StringReader(MetadataHeader + "\n" +
                             RowMeta("LU0000000001", "Foo") + "\n" +
                             RowMeta("LU0000000002", "Bar")),
            new StringReader(SummaryHeader + "\n" +
                             RowSummary("LU0000000001", "2025-04-01", "2025-04-14") + "\n" +
                             RowSummary("LU0000000002", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" +
                             RowSnap("LU0000000001") + "\n" +
                             RowSnap("LU0000000002")),
            ParsePos(PositionsHeader + "\nLU0000000002,Bar,4000,5000\n,Cash,0,0"),
            new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Any(f => f.Isin == "LU0000000002"), Is.False, "writeoff fund excluded from funds[]");
            Assert.That(result.FrozenPositions, Has.Count.EqualTo(1));
            Assert.That(result.FrozenPositions[0].Isin?.Value, Is.EqualTo("LU0000000002"));
            Assert.That(result.FrozenPositions[0].CurrentValueKr, Is.EqualTo(5000m));
            Assert.That(result.FrozenPositions[0].CostBasisKr, Is.EqualTo(4000m));
            Assert.That(result.DataQuality.WriteoffCount, Is.EqualTo(1));
        });
    }

    // ─── #8. Writeoff pinning + not held ─────────────────────────────────────
    [Test]
    public void RunInMemory_WriteoffPinningAndNotHeld_SilentlyFilteredNoFrozenEntry()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        const string md = """
            | ISIN | Fund | Layer    | Note   |
            | ---- | ---- | -------- | ------ |
            |      | Bar  | writeoff | frozen |
            """;
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-writeoff-notheld",
            new StringReader(MetadataHeader + "\n" +
                             RowMeta("LU0000000001", "Foo") + "\n" +
                             RowMeta("LU0000000002", "Bar")),
            new StringReader(SummaryHeader + "\n" +
                             RowSummary("LU0000000001", "2025-04-01", "2025-04-14") + "\n" +
                             RowSummary("LU0000000002", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" +
                             RowSnap("LU0000000001") + "\n" +
                             RowSnap("LU0000000002")),
            ParsePos(PositionsHeader + "\n,Cash,0,0"),
            new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds.Any(f => f.Isin == "LU0000000002"), Is.False);
            Assert.That(result.FrozenPositions, Is.Empty);
            Assert.That(result.DataQuality.WriteoffCount, Is.EqualTo(0));
        });
    }

    // ─── #9. Pinning name typo ───────────────────────────────────────────────
    [Test]
    public void RunInMemory_PinningNameDoesNotMatchAnyFund_WarnsAndContinues()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        const string md = """
            | ISIN | Fund        | Layer | Note   |
            | ---- | ----------- | ----- | ------ |
            |      | Typo Name X | core  | anchor |
            """;
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-typo",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo")),
            new StringReader(SummaryHeader + "\n" + RowSummary("LU0000000001", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")),
            ParsePos(PositionsHeader + "\n,Cash,0,0"),
            new StringReader(md));

        Assert.Multiple(() =>
        {
            Assert.That(result.Funds, Has.Count.EqualTo(1));
            Assert.That(result.DataQuality.Warnings.Any(w => w.Contains("Typo Name X")), Is.True);
        });
    }

    // ─── #10. NaN in summary Sharpe survives orchestrator round-trip ─────────
    [Test]
    public void RunInMemory_NaNInSummarySharpe_PreservedAsNullInOutput()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        var summary = SummaryHeader + "\n" +
            "LU0000000001,Foo,2025-04-01,2025-04-14,100,100,100,100,0.0,0.001,0.0,0.0,NaN,0.0,0.0,50.0,0.0";
        var result = sut.RunInMemory(
            "schroder", "2026-W18", "test-nan",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo")),
            new StringReader(summary),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")),
            ParsePos(PositionsHeader + "\n,Cash,0,0"),
            new StringReader(""));

        Assert.That(result.Funds[0].NavBuckets[0].Sharpe2w, Is.Null);

        // And the JSON wire format preserves null.
        var json = JsonSerializer.Serialize(result, JsonOptions.Default);
        Assert.That(json, Does.Contain("\"sharpe_2w\": null"));
    }

    // ─── Held ISIN not in metadata: must halt ────────────────────────────────
    [Test]
    public void RunInMemory_HeldIsinNotInMetadata_Throws()
    {
        var sut = _fixture.Create<DataLoaderAgent>();
        Assert.Throws<DataLoaderHaltException>(() => sut.RunInMemory(
            "schroder", "2026-W18", "test-orphan",
            new StringReader(MetadataHeader + "\n" + RowMeta("LU0000000001", "Foo")),
            new StringReader(SummaryHeader + "\n" + RowSummary("LU0000000001", "2025-04-01", "2025-04-14")),
            new StringReader(SnapshotHeader + "\n" + RowSnap("LU0000000001")),
            ParsePos(PositionsHeader + "\nLU9999999999,Ghost,1000,1000\n,Cash,0,0"),
            new StringReader("")));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private static string RowMeta(string isin, string name) =>
        $"{isin},{name},Co,SEK,Cat,EQUITY_FUND,false,ACTIVE,1.0,0.5,3,4,0.5,10.0,FIVE_YEAR,1000000,100";

    private static string RowSummary(string isin, string periodStart, string periodEnd) =>
        $"{isin},Name,{periodStart},{periodEnd},100,105,106,99,5.0,10.0,-1.0,-0.5,1.2,2.0,-1.0,55.0,0.1";

    private static string RowSnap(string isin) =>
        $"{isin},2026-04-30,5.0,10.0,0.5,-2.0,15.0,12.0,1.0,-3.0";
}
