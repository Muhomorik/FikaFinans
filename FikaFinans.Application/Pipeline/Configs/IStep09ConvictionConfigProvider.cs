namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// Seam over step 9's conviction settings. The step knows it needs a
/// <see cref="UniverseEnricherConfig"/>, not that it lives in a JSON file on the desktop or
/// somewhere else in the cloud.
/// </summary>
/// <remarks>
/// Also the one place that decides what a missing config means — each implementation
/// either falls back to defaults or fails, so callers never decide it themselves. The
/// weights check is not here: the agent keeps it, so a bad edit fails the run, not the read.
/// </remarks>
public interface IStep09ConvictionConfigProvider
{
    /// <summary>Returns the settings step 9 runs with, read fresh on every call.</summary>
    Task<UniverseEnricherConfig> GetConfigAsync(CancellationToken ct = default);
}
