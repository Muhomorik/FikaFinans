using System.Text.Json;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Configs;

/// <summary>
/// <see cref="IStep10PortfolioConfigProvider"/> backed by <c>config-10-portfolio.json</c>,
/// located through <see cref="IPathsService"/> — the same file the tab's config editor writes.
/// </summary>
/// <remarks>
/// Read on every call rather than cached, so an edit in the tab applies to the next run
/// without a restart. The file is a few kilobytes; rereading it costs nothing.
/// </remarks>
public sealed class JsonBackedStep10PortfolioConfigProvider : IStep10PortfolioConfigProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public JsonBackedStep10PortfolioConfigProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<PortfolioConstructorConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var path = _paths.Config10PortfolioJson;

        // Falls back rather than failing, as steps 4 and 9's providers do: startup writes the
        // default file, so a missing one means it was deleted by hand — the defaults are what
        // that file would have held. Warned, because the run then ignores whatever was set.
        if (!File.Exists(path))
        {
            _logger.Warn("Step 10 config not found, using defaults — {0}", path);
            return PortfolioConstructorConfig.Default;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        PortfolioConstructorConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<PortfolioConstructorConfig>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            // Thrown, unlike a missing file: a broken edit is a mistake to surface, and
            // quietly running on defaults would hide it.
            throw new InvalidDataException($"Step 10 config is not valid JSON — {path}", ex);
        }

        if (config is null)
            throw new InvalidDataException($"Step 10 config is empty — {path}");

        // The values the tab's editor is most likely to change; the version is left for the
        // agent's check to judge.
        _logger.Trace(
            "Step 10 config read — {0}: version {1}, cash floor {2}, macro override {3}, " +
            "max position {4}, max sector {5}, min trade {6} kr, skip buy below {7}, skip sell below {8}",
            path, config.Meta?.ConfigVersion ?? "(none)", config.CashPolicy.FloorPct,
            config.CashPolicy.MacroOverrideEnabled, config.Constraints.MaxPositionPctOfPortfolio,
            config.Constraints.MaxSectorPctOfPortfolio, config.Constraints.MinTradeKr,
            config.Guards.SkipBuyBelowConviction, config.Guards.SkipSellBelowConviction);

        return config;
    }
}
