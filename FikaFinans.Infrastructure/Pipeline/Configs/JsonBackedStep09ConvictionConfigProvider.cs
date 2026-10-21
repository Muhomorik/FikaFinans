using System.Text.Json;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Configs;

/// <summary>
/// <see cref="IStep09ConvictionConfigProvider"/> backed by <c>config-09-conviction.json</c>,
/// located through <see cref="IPathsService"/> — the same file the tab's config editor writes.
/// </summary>
/// <remarks>
/// Read on every call rather than cached, so an edit in the tab applies to the next run
/// without a restart. The file is a few kilobytes; rereading it costs nothing.
/// </remarks>
public sealed class JsonBackedStep09ConvictionConfigProvider : IStep09ConvictionConfigProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public JsonBackedStep09ConvictionConfigProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<UniverseEnricherConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var path = _paths.Config09ConvictionJson;

        // Falls back rather than failing, as step 4's provider does: startup writes the
        // default file, so a missing one means it was deleted by hand — the defaults are what
        // that file would have held. Warned, because the run then ignores whatever was set.
        if (!File.Exists(path))
        {
            _logger.Warn("Step 9 config not found, using defaults — {0}", path);
            return UniverseEnricherConfig.Default;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        UniverseEnricherConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<UniverseEnricherConfig>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            // Thrown, unlike a missing file: a broken edit is a mistake to surface, and
            // quietly running on defaults would hide it.
            throw new InvalidDataException($"Step 9 config is not valid JSON — {path}", ex);
        }

        if (config is null)
            throw new InvalidDataException($"Step 9 config is empty — {path}");

        // The weights go in the line because they are what the tab's editor changes; the
        // sum is left for the agent's check to judge.
        var w = config.Weights;
        _logger.Trace(
            "Step 9 config read — {0}: weights signal {1}, metrics {2}, macro {3}, thesis {4}, universe {5}",
            path, w.SignalStrength, w.MetricsQuality, w.MacroAlignment, w.ThesisValidity, w.UniverseContext);

        return config;
    }
}
