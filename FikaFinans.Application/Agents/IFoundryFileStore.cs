namespace FikaFinans.Application.Agents;

/// <summary>
/// Port for the Foundry file-upload pipeline. Two write paths:
/// <see cref="EnsureFilesUploadedAsync"/> (implicit, runs before every comparison) and
/// <see cref="ForceReuploadAllAsync"/> (explicit, wired to the Refresh-all button).
/// Read-only inspection lives in <see cref="GetStatusAsync"/>.
/// </summary>
public interface IFoundryFileStore
{
    /// <summary>Absolute path to the JSON sidecar tracking uploaded fileIds (exposed so the UI can show it).</summary>
    string SidecarFilePath { get; }

    /// <summary>
    /// Inspect each logical file's status without calling the Foundry API. Pure local
    /// read of file mtimes + the JSON sidecar.
    /// </summary>
    Task<IReadOnlyList<FoundryFileEntry>> GetStatusAsync(
        FundDataFileSet fileSet,
        CancellationToken ct = default);

    /// <summary>
    /// Implicit upload path. Uploads anything <see cref="FoundryFileStatus.Stale"/> or
    /// <see cref="FoundryFileStatus.NotUploaded"/>. Returns the resolved
    /// <c>logicalName → fileId</c> map for use by an agent run.
    /// Throws if any file is <see cref="FoundryFileStatus.Missing"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> EnsureFilesUploadedAsync(
        FundDataFileSet fileSet,
        CancellationToken ct = default);

    /// <summary>
    /// Explicit upload path. Deletes existing fileIds via the Foundry Files API and
    /// re-uploads every file regardless of mtime. <paramref name="progress"/> reports
    /// each row as it completes.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ForceReuploadAllAsync(
        FundDataFileSet fileSet,
        IProgress<FoundryFileEntry>? progress = null,
        CancellationToken ct = default);
}
