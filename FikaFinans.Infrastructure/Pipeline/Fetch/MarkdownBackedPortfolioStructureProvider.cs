using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Fetch;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Csv;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Fetch;

/// <summary>
/// <see cref="IPortfolioStructureProvider"/> backed by the hand-written
/// <c>portfolio_structure.md</c>, located through <see cref="IPathsService"/>.
/// </summary>
/// <remarks>
/// Library code: awaits with <c>ConfigureAwait(false)</c> and honours cancellation at the
/// IO boundary. The parser itself is synchronous, so the file is read in one go rather
/// than streamed — it is a short hand-edited table, not a data export.
/// </remarks>
public sealed class MarkdownBackedPortfolioStructureProvider : IPortfolioStructureProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;
    private readonly PortfolioStructureMdParser _parser;

    public MarkdownBackedPortfolioStructureProvider(
        ILogger logger, IPathsService paths, PortfolioStructureMdParser parser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <inheritdoc />
    public async Task<PortfolioStructure> GetStructureAsync(CancellationToken ct = default)
    {
        var path = _paths.PortfolioStructureMd;

        // A missing file is an ordinary outcome — nobody has pinned anything yet — so it
        // reads as "no overrides" rather than throwing.
        if (!File.Exists(path))
        {
            _logger.Trace("Portfolio structure file not found — {0}", path);
            return new PortfolioStructure { Pinnings = Array.Empty<PinnedFund>() };
        }

        var markdown = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        using var reader = new StringReader(markdown);
        var structure = _parser.Parse(reader);

        _logger.Trace("Portfolio structure read — {0} pinning(s) from {1}", structure.Pinnings.Count, path);

        return structure;
    }
}
