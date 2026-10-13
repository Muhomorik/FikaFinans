using System.Text.Json;

using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Infrastructure.Pipeline.Json;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Configs;

/// <summary>
/// <see cref="IStep04SignalsConfigProvider"/> backed by <c>config-04-signals.json</c>,
/// located through <see cref="IPathsService"/> — the same file the tab's config editor writes.
/// </summary>
/// <remarks>
/// Read on every call rather than cached, so an edit in the tab applies to the next fund
/// without a restart. The file is a few kilobytes; rereading it costs nothing.
/// </remarks>
public sealed class JsonBackedStep04SignalsConfigProvider : IStep04SignalsConfigProvider
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public JsonBackedStep04SignalsConfigProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<SignalScorerConfig> GetConfigAsync(CancellationToken ct = default)
    {
        var path = _paths.Config04SignalsJson;

        // Falls back rather than failing: startup writes the default file, so a missing one
        // means it was deleted by hand — the defaults are what that file would have held.
        // Warned, not traced, because the run then ignores whatever the user meant to set.
        if (!File.Exists(path))
        {
            _logger.Warn("Step 4 config not found, using defaults — {0}", path);
            return SignalScorerConfig.Default;
        }

        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        SignalScorerConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<SignalScorerConfig>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            // Thrown, unlike a missing file: a broken edit is a mistake to surface, and
            // quietly running on defaults would hide it.
            throw new InvalidDataException($"Step 4 config is not valid JSON — {path}", ex);
        }

        if (config is null)
            throw new InvalidDataException($"Step 4 config is empty — {path}");

        _logger.Trace("Step 4 config read — {0}", path);

        if (config.SellWeaknessAnyOf is { Count: > 0 })
            return config;

        // The contract's sell rule is any one of three triggers. An empty list would switch
        // every exit off without a word, so it reads as "not set" — the agent's own file read
        // has always done the same.
        _logger.Warn("Step 4 config has no sell triggers, using the default three — {0}", path);

        return new SignalScorerConfig
        {
            BuyStrength = config.BuyStrength,
            SellWeaknessAnyOf = SellTrigger.Defaults,
            DataQualityHandling = config.DataQualityHandling,
        };
    }
}
