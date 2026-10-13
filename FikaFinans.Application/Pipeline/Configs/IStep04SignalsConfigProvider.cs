namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// Seam over step 4's settings. The step knows it needs a <see cref="SignalScorerConfig"/>,
/// not that it lives in a JSON file on the desktop or somewhere else in the cloud.
/// </summary>
/// <remarks>
/// Also the one place that decides what a missing config means — each implementation
/// either falls back to defaults or fails, so callers never decide it themselves. An empty
/// sell-trigger list falls back to <see cref="SellTrigger.Defaults"/>: the contract's sell
/// rule is any one of three triggers, and an empty list would switch every exit off.
/// </remarks>
public interface IStep04SignalsConfigProvider
{
    /// <summary>Returns the settings step 4 runs with, read fresh on every call.</summary>
    Task<SignalScorerConfig> GetConfigAsync(CancellationToken ct = default);
}
