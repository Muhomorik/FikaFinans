using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Infrastructure.Pipeline.Csv;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// <see cref="IFundMetadataProvider"/> backed by YieldRacoon's weekly
/// <c>YieldRaccoon_metadata_{family}_{iso_week}.csv</c> exports, located under
/// the inputs folder through <see cref="IPathsService"/>.
/// </summary>
/// <remarks>
/// One file per week, so this is the only implementation that can honour
/// <c>isoWeek</c>. Library code: awaits with <c>ConfigureAwait(false)</c> and
/// honours cancellation at IO boundaries.
/// </remarks>
public sealed class YieldRaccoonCsvBackedMetadataProvider : IFundMetadataProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;
    private readonly MetadataCsvParser _parser;

    public YieldRaccoonCsvBackedMetadataProvider(ILogger logger, IPathsService paths, MetadataCsvParser parser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <inheritdoc />
    public async Task<FundMetadata?> GetMetadataAsync(
        Isin isin, Company company, IsoWeek isoWeek, CancellationToken ct = default)
    {
        // The export filename's company segment is lower-cased; the column
        // inside preserves original case.
        var path = _paths.MetadataCsv(company.Value.ToLowerInvariant(), isoWeek.Value);

        // A missing export is an ordinary outcome — that company/week was never
        // exported — so the fund reads as absent rather than throwing.
        if (!File.Exists(path))
        {
            _logger.Trace("YR metadata CSV not found — {0}", path);
            return null;
        }

        // Read async so cancellation lands at the IO boundary; the parser itself
        // is synchronous.
        var csv = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        using var reader = new StringReader(csv);

        return _parser.Parse(reader).FirstOrDefault(m => m.Isin == isin);
    }
}
