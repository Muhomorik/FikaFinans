namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// Seam over step 2's settings. The step knows it needs a
/// <see cref="MetricsCalculatorConfig"/>, not that it lives in a JSON file on the desktop
/// or somewhere else in the cloud.
/// </summary>
/// <remarks>
/// Also the one place that decides what a missing config means — each implementation
/// either falls back to defaults or fails, so callers never decide it themselves.
/// </remarks>
public interface IStep02MetricsConfigProvider
{
    /// <summary>Returns the settings step 2 runs with, read fresh on every call.</summary>
    Task<MetricsCalculatorConfig> GetConfigAsync(CancellationToken ct = default);
}
