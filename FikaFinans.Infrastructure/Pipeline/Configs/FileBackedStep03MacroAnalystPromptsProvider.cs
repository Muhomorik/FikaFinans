using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Configs;

using NLog;

namespace FikaFinans.Infrastructure.Pipeline.Configs;

/// <summary>
/// <see cref="IStep03MacroAnalystPromptsProvider"/> backed by the three files in
/// <see cref="IPathsService.MacroAnalystPromptsDir"/> — <c>system.md</c>,
/// <c>user-template.md</c> and <c>retry.md</c>, the folder the agent reads today.
/// </summary>
/// <remarks>
/// A missing file is first extracted from the embedded default, so a fresh install works and
/// a deleted prompt comes back as shipped. An existing file is never overwritten — edits
/// survive a redeploy. Read on every call rather than cached, so an edited prompt applies to
/// the next LLM call without a restart.
/// </remarks>
public sealed class FileBackedStep03MacroAnalystPromptsProvider : IStep03MacroAnalystPromptsProvider
{
    private const string SystemFile = "system.md";
    private const string UserTemplateFile = "user-template.md";
    private const string RetryFile = "retry.md";

    private readonly ILogger _logger;
    private readonly IPathsService _paths;

    public FileBackedStep03MacroAnalystPromptsProvider(ILogger logger, IPathsService paths)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <inheritdoc />
    public async Task<Step03MacroAnalystPrompts> GetPromptsAsync(CancellationToken ct = default)
    {
        var dir = _paths.MacroAnalystPromptsDir;
        Directory.CreateDirectory(dir);

        var system       = await ReadAsync(dir, SystemFile, ct).ConfigureAwait(false);
        var userTemplate = await ReadAsync(dir, UserTemplateFile, ct).ConfigureAwait(false);
        var retry        = await ReadAsync(dir, RetryFile, ct).ConfigureAwait(false);

        _logger.Trace("Step 3 prompts read — {0}", dir);

        return new Step03MacroAnalystPrompts(system, userTemplate, retry);
    }

    private async Task<string> ReadAsync(string dir, string fileName, CancellationToken ct)
    {
        var path = Path.Combine(dir, fileName);

        if (!File.Exists(path))
        {
            await ExtractDefaultAsync(path, fileName, ct).ConfigureAwait(false);
            _logger.Info("Step 3 prompt not found, default extracted — {0}", path);
        }

        var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

        // Thrown, unlike a missing file: an emptied prompt is a mistake to surface, and
        // sending the LLM nothing would hide it behind a confusing response.
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Step 3 prompt is empty — {path}");

        return text;
    }

    private static async Task ExtractDefaultAsync(string path, string fileName, CancellationToken ct)
    {
        var asm = typeof(FileBackedStep03MacroAnalystPromptsProvider).Assembly;
        var resourceName = $"Defaults.Prompts.MacroAnalyst.{fileName}";

        await using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in {asm.GetName().Name}. " +
                "Ensure the .md files are included as EmbeddedResource in the Infrastructure project.");
        await using var fs = File.Create(path);
        await stream.CopyToAsync(fs, ct).ConfigureAwait(false);
    }
}
