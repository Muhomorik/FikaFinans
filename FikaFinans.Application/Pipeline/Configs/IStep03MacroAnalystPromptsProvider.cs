namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// Seam over step 3's prompts. The step knows it needs the
/// <see cref="Step03MacroAnalystPrompts"/>, not that they are files in a folder on the
/// desktop or something else in the cloud.
/// </summary>
/// <remarks>
/// Also the one place that decides what a missing prompt means — each implementation
/// either falls back to the embedded default or fails, so callers never decide it themselves.
/// </remarks>
public interface IStep03MacroAnalystPromptsProvider
{
    /// <summary>Returns the prompts step 3 runs with, read fresh on every call.</summary>
    /// <exception cref="InvalidDataException">A prompt is present but empty.</exception>
    Task<Step03MacroAnalystPrompts> GetPromptsAsync(CancellationToken ct = default);
}
