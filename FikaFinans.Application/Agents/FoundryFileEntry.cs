namespace FikaFinans.Application.Agents;

/// <summary>
/// Status of one logical fund-data file relative to its uploaded counterpart.
/// </summary>
public enum FoundryFileStatus
{
    /// <summary>
    /// Local file is missing — the run will fail until it's added.
    /// </summary>
    Missing,

    /// <summary>
    /// Local file exists but has never been uploaded.
    /// </summary>
    NotUploaded,

    /// <summary>
    /// Local file's mtime is newer than the cached upload.
    /// </summary>
    Stale,

    /// <summary>
    /// Local file's mtime matches the cached upload.
    /// </summary>
    Fresh
}

/// <summary>
/// One row of the upload-status strip in the WPF view. Returned by
/// <see cref="IFoundryFileStore.GetStatusAsync"/> and consumed directly by
/// <c>FileEntryViewModel</c>. Both the local-disk facts and the Foundry-side
/// facts are surfaced so the UI can show *why* a file is Stale (mtime/size diff).
/// </summary>
public sealed record FoundryFileEntry(
    string LogicalName,
    string LocalPath,
    bool LocalExists,
    DateTimeOffset? LocalMtime,
    long? LocalSize,
    string? UploadedFileId,
    DateTimeOffset? UploadedAt,
    DateTimeOffset? UploadedSourceMtime,
    long? UploadedSourceSize,
    FoundryFileStatus Status);