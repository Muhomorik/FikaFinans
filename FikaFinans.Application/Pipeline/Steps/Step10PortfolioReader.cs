using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

using NLog;

namespace FikaFinans.Application.Pipeline.Steps;

/// <summary>
/// <see cref="IStep10PortfolioReader"/> over <see cref="IHoldingsProvider"/> and
/// <see cref="IPortfolioStructureProvider"/> — the two seams step 1 reads the book through, so
/// each host backs step 10 with the same sources it backs step 1 with.
/// </summary>
/// <remarks>
/// Classifies as step 1 does, through <see cref="PortfolioStructure.LayerOf"/>: pinned
/// <c>core</c> is core, pinned <c>writeoff</c> is frozen, anything else active. One
/// difference: step 1 freezes writeoff holdings among its universe's metadata, this freezes
/// every writeoff holding — a writeoff fund has no record to say whether it is in the universe.
/// </remarks>
public sealed class Step10PortfolioReader : IStep10PortfolioReader
{
    /// <summary>The <see cref="FrozenPosition.Reason"/> step 1 writes.</summary>
    private const string FrozenReason = "frozen — cannot trade";

    private readonly ILogger _logger;
    private readonly IHoldingsProvider _holdings;
    private readonly IPortfolioStructureProvider _structure;

    public Step10PortfolioReader(
        ILogger logger,
        IHoldingsProvider holdings,
        IPortfolioStructureProvider structure)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _holdings = holdings ?? throw new ArgumentNullException(nameof(holdings));
        _structure = structure ?? throw new ArgumentNullException(nameof(structure));
    }

    /// <inheritdoc />
    public async Task<Step10Portfolio> ReadAsync(IReadOnlyList<FundRecord> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var holdings = await _holdings.GetHoldingsAsync(ct).ConfigureAwait(false);
        var structure = await _structure.GetStructureAsync(ct).ConfigureAwait(false);

        // Step 10 has no data-quality block to put these in, so the log is where they go.
        foreach (var warning in holdings.Warnings)
            _logger.Warn("Step 10 portfolio — holdings: {0}", warning);

        var positionByIsin = holdings.Holdings.ToDictionary(p => p.Isin);

        var funds = new List<FundRecord>(records.Count);
        var recordIsins = new HashSet<Isin>();
        var writeoffIsins = new HashSet<Isin>();

        foreach (var record in records)
        {
            recordIsins.Add(record.Isin);
            var pinned = structure.LayerOf(record.Isin, record.Metadata.Name);

            // Step 1 writes no record for a writeoff fund; one here is a pin added since. Its
            // holding, if any, is frozen below, matched by ISIN even if the pin is by name.
            if (pinned == PinnedLayer.Writeoff)
            {
                writeoffIsins.Add(record.Isin);
                _logger.Warn("Step 10 portfolio — {0} has a record but is pinned writeoff, left out", record.Isin.Value);
                continue;
            }

            positionByIsin.TryGetValue(record.Isin, out var position);
            var layer = pinned == PinnedLayer.Core ? FundLayer.Core : FundLayer.Active;
            var current = WithHolding(record, position, layer);

            if (current.CurrentlyHeld != record.CurrentlyHeld
                || current.CurrentValueKr != record.CurrentValueKr
                || current.Layer != record.Layer)
            {
                _logger.Debug(
                    "Step 10 portfolio — {0}: record held={1} value={2} layer={3}, now held={4} value={5} layer={6}",
                    record.Isin.Value, record.CurrentlyHeld, record.CurrentValueKr, record.Layer,
                    current.CurrentlyHeld, current.CurrentValueKr, current.Layer);
            }

            funds.Add(current);
        }

        var frozen = new List<FrozenPosition>();
        var heldWithoutRecord = new List<Position>();

        foreach (var position in holdings.Holdings)
        {
            if (writeoffIsins.Contains(position.Isin)
                || structure.LayerOf(position.Isin, position.Name) == PinnedLayer.Writeoff)
            {
                frozen.Add(new FrozenPosition
                {
                    Name = position.Name ?? position.Isin.Value,
                    Isin = position.Isin,
                    CurrentValueKr = position.CurrentValueKr,
                    CostBasisKr = position.CostBasisKr,
                    Reason = FrozenReason,
                });
                continue;
            }

            if (!recordIsins.Contains(position.Isin))
            {
                heldWithoutRecord.Add(position);
                _logger.Warn("Step 10 portfolio — {0} held at {1} kr but has no step 9 record, not counted",
                    position.Isin.Value, position.CurrentValueKr);
            }
        }

        _logger.Debug(
            "Step 10 portfolio — {0} record(s), {1} held, {2} frozen ({3} kr), cash {4} kr, {5} held without record",
            funds.Count, funds.Count(f => f.CurrentlyHeld), frozen.Count, frozen.Sum(f => f.CurrentValueKr),
            holdings.CashAvailableKr, heldWithoutRecord.Count);

        return new Step10Portfolio(funds, frozen, holdings.CashAvailableKr, heldWithoutRecord);
    }

    /// <summary>
    /// The record with the holdings' view of it — the rest copied through, as the agents copy.
    /// A new <see cref="FundRecord"/> field must be added here too; a test round-trips the
    /// record to catch one missed.
    /// </summary>
    private static FundRecord WithHolding(FundRecord fund, Position? position, FundLayer layer) => new()
    {
        Isin                 = fund.Isin,
        Metadata             = fund.Metadata,
        NavBuckets           = fund.NavBuckets,
        Snapshot             = fund.Snapshot,
        CurrentlyHeld        = position is not null,
        CurrentValueKr       = position?.CurrentValueKr,
        CostBasisKr          = position?.CostBasisKr,
        Layer                = layer,
        Metrics              = fund.Metrics,
        Signal               = fund.Signal,
        RuleFired            = fund.RuleFired,
        CriteriaEvaluation   = fund.CriteriaEvaluation,
        MacroAlignment       = fund.MacroAlignment,
        MatchedTheme         = fund.MatchedTheme,
        PromotedToForming    = fund.PromotedToForming,
        PromotionReason      = fund.PromotionReason,
        Catalyst             = fund.Catalyst,
        ThesisValidity       = fund.ThesisValidity,
        ThesisRationale      = fund.ThesisRationale,
        ThesisMethod         = fund.ThesisMethod,
        Recommendation       = fund.Recommendation,
        RecommendationReason = fund.RecommendationReason,
        ConvictionScore      = fund.ConvictionScore,
        ConvictionBreakdown  = fund.ConvictionBreakdown,
        UniverseRank         = fund.UniverseRank,
        Alternatives         = fund.Alternatives,
        RotationPairId       = fund.RotationPairId,
    };
}
