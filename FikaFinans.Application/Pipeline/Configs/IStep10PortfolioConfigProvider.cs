namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// Seam over step 10's portfolio settings — cash policy, constraints, conviction guards,
/// sizing. The step knows it needs a <see cref="PortfolioConstructorConfig"/>, not that it
/// lives in a JSON file on the desktop or somewhere else in the cloud.
/// </summary>
/// <remarks>
/// Also the one place that decides what a missing config means — each implementation
/// either falls back to defaults or fails, so callers never decide it themselves. The
/// <c>config_version</c> check is not here: the agent keeps it, so a bad edit fails the run,
/// not the read.
/// </remarks>
public interface IStep10PortfolioConfigProvider
{
    /// <summary>Returns the settings step 10 runs with, read fresh on every call.</summary>
    Task<PortfolioConstructorConfig> GetConfigAsync(CancellationToken ct = default);
}
