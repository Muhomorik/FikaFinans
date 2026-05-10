using System.Reflection;
using FikaFinans.Application.Paths;
using NLog;

namespace FikaFinans.Infrastructure;

/// <summary>
/// Runs once at app startup (after DI is built) to ensure every directory and default
/// config file exists on disk before any pipeline agent or UI component needs them.
/// </summary>
public sealed class AppStartupInitializer
{
    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public AppStartupInitializer(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void Initialize()
    {
        EnsureDirectories();
        EnsureDefaultConfigs();
    }

    private void EnsureDirectories()
    {
        // inputs/ and stepOutputs/ are also created pre-DI in App.EnsureDefaultDirectories,
        // but we do it here too so settings-backed overrides take effect.
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.Config02MetricsJson)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.DataLoaderOutput("placeholder", "placeholder"))!);

        // MacroAnalyst prompt files — MacroAnalystAgent also creates this in its ctor,
        // but creating it here ensures it exists even before the agent is first resolved.
        Directory.CreateDirectory(_paths.MacroAnalystPromptsDir);

        _logger.Debug("AppStartupInitializer: directories ready");
    }

    private void EnsureDefaultConfigs()
    {
        var asm = typeof(AppStartupInitializer).Assembly;
        var configs = new[]
        {
            (_paths.Config02MetricsJson,    "Defaults.Configs.config-02-metrics.json"),
            (_paths.Config04SignalsJson,    "Defaults.Configs.config-04-signals.json"),
            (_paths.Config09ConvictionJson, "Defaults.Configs.config-09-conviction.json"),
            (_paths.Config10PortfolioJson,  "Defaults.Configs.config-10-portfolio.json"),
        };

        foreach (var (path, resource) in configs)
        {
            if (File.Exists(path)) continue;

            using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resource}' not found in {asm.GetName().Name}. " +
                    "Ensure the JSON files are included as EmbeddedResource in the Infrastructure project.");
            using var fs = File.Create(path);
            stream.CopyTo(fs);
            _logger.Info("Created default config: {0}", Path.GetFileName(path));
        }
    }
}
