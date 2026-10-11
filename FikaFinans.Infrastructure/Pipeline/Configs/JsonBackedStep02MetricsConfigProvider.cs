using System.Text.Json;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Configs;

/// <summary>
/// <see cref="IStep02MetricsConfigProvider"/> backed by <c>config-02-metrics.json</c>,
/// located through <see cref="IPathsService"/> — the same file the tab's config editor writes.
/// </summary>
/// <remarks>
/// Read on every call rather than cached, so an edit in the tab applies to the next fund
/// without a restart. The file is a few hundred bytes; rereading it costs nothing.
/// </remarks>
public sealed class JsonBackedStep02MetricsConfigProvider : IStep02MetricsConfigProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public JsonBackedStep02MetricsConfigProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<MetricsCalculatorConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var path = _paths.Config02MetricsJson;

        // Falls back rather than failing: startup writes the default file, so a missing one
        // means it was deleted by hand — the defaults are what that file would have held.
        // Warned, not traced, because the run then ignores whatever the user meant to set.
        if (!File.Exists(path))
        {
            _logger.Warn("Step 2 config not found, using defaults — {0}", path);
            return MetricsCalculatorConfig.Default;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        MetricsCalculatorConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<MetricsCalculatorConfig>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            // Thrown, unlike a missing file: a broken edit is a mistake to surface, and
            // quietly running on defaults would hide it.
            throw new InvalidDataException($"Step 2 config is not valid JSON — {path}", ex);
        }

        if (config is null)
            throw new InvalidDataException($"Step 2 config is empty — {path}");

        _logger.Trace("Step 2 config read — {0}", path);

        return config;
    }
}
