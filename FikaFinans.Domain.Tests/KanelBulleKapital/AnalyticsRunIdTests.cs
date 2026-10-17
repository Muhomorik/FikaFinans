using System.Text.Json;
using System.Text.Json.Serialization;
using FikaFinans.Domain.KanelBulleKapital;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Domain.Tests.KanelBulleKapital;

/// <summary>
/// The copies of KanelBulleKapital's three run ids. The JSON stays a plain string, so the
/// report files and stored contexts read back unchanged.
/// </summary>
[TestFixture]
[TestOf(typeof(WeeklySummaryRunId))]
[TestOf(typeof(SubstitutionChainRunId))]
[TestOf(typeof(OpportunityScanRunId))]
public sealed class AnalyticsRunIdTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    // ── WeeklySummaryRunId ────────────────────────────────────────────

    [Test]
    public void WeeklySummaryRunId_Json_RoundTripsAsPlainString()
    {
        // Arrange — not a GUID on purpose: the format is the producer's, not checked here.
        WeeklySummaryRunId id = "ws-run-23";

        // Act
        var json = JsonSerializer.Serialize(id, Web);
        var back = JsonSerializer.Deserialize<WeeklySummaryRunId>(json, Web);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("\"ws-run-23\""));
            Assert.That(back, Is.EqualTo(id));
        });
    }

    [Test]
    public void WeeklySummaryRunId_Default_IsEmpty()
    {
        // Act
        var id = default(WeeklySummaryRunId);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(id.IsEmpty, Is.True);
            Assert.That(id.ToString(), Is.Empty);
        });
    }

    // ── SubstitutionChainRunId ────────────────────────────────────────

    [Test]
    public void SubstitutionChainRunId_Json_RoundTripsAsPlainString()
    {
        // Arrange
        SubstitutionChainRunId id = "ws-run-23-chain";

        // Act
        var json = JsonSerializer.Serialize(id, Web);
        var back = JsonSerializer.Deserialize<SubstitutionChainRunId>(json, Web);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("\"ws-run-23-chain\""));
            Assert.That(back, Is.EqualTo(id));
        });
    }

    // ── OpportunityScanRunId ──────────────────────────────────────────

    [Test]
    public void OpportunityScanRunId_Json_RoundTripsAsPlainString()
    {
        // Arrange
        OpportunityScanRunId id = "ws-run-23-targets";

        // Act
        var json = JsonSerializer.Serialize(id, Web);
        var back = JsonSerializer.Deserialize<OpportunityScanRunId>(json, Web);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(json, Is.EqualTo("\"ws-run-23-targets\""));
            Assert.That(back, Is.EqualTo(id));
        });
    }

    // ── On the reports ────────────────────────────────────────────────

    [Test]
    public void SubstitutionChainRun_ExportedJson_ReadsBothIdsTyped()
    {
        // Arrange — the shape KanelBulleKapital exports, unchanged.
        const string json = """
            {
              "reportType": "substitution-chain",
              "runId": "chain-1",
              "status": "Success",
              "periodIsoWeek": "2026-W18",
              "weeklySummaryRunId": "ws-1",
              "chains": []
            }
            """;

        // Act
        var run = JsonSerializer.Deserialize<SubstitutionChainRun>(json, Web)!;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(run.RunId, Is.EqualTo(new SubstitutionChainRunId("chain-1")));
            Assert.That(run.WeeklySummaryRunId, Is.EqualTo(new WeeklySummaryRunId("ws-1")));
        });
    }

    [Test]
    public void SourceRunIds_Json_KeepsItsPropertyNames()
    {
        // Arrange — the stored context's shape: a typed id must not change what is on disk.
        var ids = new SourceRunIds
        {
            WeeklySummaryRunId = "ws-1",
            SubstitutionChainRunId = "sc-1",
            RotationTargetsRunId = "rt-1",
        };

        // Act
        var json = JsonSerializer.Serialize(ids, Web);

        // Assert
        Assert.That(json, Is.EqualTo(
            """{"weeklySummaryRunId":"ws-1","substitutionChainRunId":"sc-1","rotationTargetsRunId":"rt-1"}"""));
    }
}
