using FikaFinans.Application.Pipeline.Macro;
using FikaFinans.Domain.KanelBulleKapital;
using FikaFinans.Domain.Macro;

namespace FikaFinans.Application.Tests.Pipeline.Macro;

[TestFixture]
[TestOf(typeof(MacroContextSingleFlight))]
public sealed class MacroContextSingleFlightTests
{
    private static readonly WeeklySummaryRunId Run22 = new("run-22");
    private static readonly WeeklySummaryRunId Run23 = new("run-23");

    private static MacroContext Context(WeeklySummaryRunId runId) => new()
    {
        GeneratedAt = "2026-06-05T10:00:00.0000000+00:00",
        IsoWeek = "2026-W23",
        ConfigVersion = "1.0.0",
        SourceRunIds = new SourceRunIds
        {
            WeeklySummaryRunId = runId,
            SubstitutionChainRunId = $"{runId.Value}-chain",
            RotationTargetsRunId = $"{runId.Value}-targets",
        },
        MacroRegime = MacroRegime.Mixed,
        RegimeConfidence = 0.5m,
        NetMoodInput = MarketSentiment.Mixed,
        Catalysts = [],
        RotationThemes = [],
    };

    [Test]
    public async Task RunAsync_SameKeyWhileRunning_SharesOneCompute()
    {
        // Arrange
        var sut = new MacroContextSingleFlight();
        var release = new TaskCompletionSource<MacroContext>();
        var computes = 0;
        Task<MacroContext> Compute(CancellationToken _) { Interlocked.Increment(ref computes); return release.Task; }

        // Act
        var first = sut.RunAsync(Run23, Compute);
        var second = sut.RunAsync(Run23, Compute);
        release.SetResult(Context(Run23));
        var results = await Task.WhenAll(first, second);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(computes, Is.EqualTo(1));
            Assert.That(results[0], Is.SameAs(results[1]));
        });
    }

    [Test]
    public async Task RunAsync_DifferentKeys_ComputeEach()
    {
        // Arrange
        var sut = new MacroContextSingleFlight();
        var computes = 0;
        Task<MacroContext> Compute(CancellationToken _)
        {
            Interlocked.Increment(ref computes);
            return Task.FromResult(Context(new WeeklySummaryRunId("any")));
        }

        // Act
        await Task.WhenAll(sut.RunAsync(Run22, Compute), sut.RunAsync(Run23, Compute));

        // Assert
        Assert.That(computes, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_AfterACompleteCompute_ComputesAgain()
    {
        // Arrange — finished entries are dropped; the store is what remembers a result.
        var sut = new MacroContextSingleFlight();
        var computes = 0;
        Task<MacroContext> Compute(CancellationToken _)
        {
            Interlocked.Increment(ref computes);
            return Task.FromResult(Context(Run23));
        }

        // Act
        await sut.RunAsync(Run23, Compute);
        await sut.RunAsync(Run23, Compute);

        // Assert
        Assert.That(computes, Is.EqualTo(2));
    }

    [Test]
    public async Task RunAsync_ComputeFails_EveryWaiterSeesItAndTheNextCallRetries()
    {
        // Arrange
        var sut = new MacroContextSingleFlight();
        var release = new TaskCompletionSource<MacroContext>();
        var first = sut.RunAsync(Run23, _ => release.Task);
        var second = sut.RunAsync(Run23, _ => release.Task);

        // Act
        release.SetException(new InvalidOperationException("LLM down"));

        // Assert — a failure is not remembered: the next call starts a fresh compute.
        Assert.ThrowsAsync<InvalidOperationException>(() => first);
        Assert.ThrowsAsync<InvalidOperationException>(() => second);
        var retried = await sut.RunAsync(Run23, _ => Task.FromResult(Context(Run23)));
        Assert.That(retried.SourceRunIds.WeeklySummaryRunId, Is.EqualTo(Run23));
    }
}
