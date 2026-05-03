using Azure.AI.Extensions.OpenAI;
using NLog;
using OpenAI.Files;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

/// <summary>
/// Per-test file uploader. Uploads via the OpenAI Files API with
/// <c>purpose=Assistants</c> (so Code Interpreter can mount the files at
/// <c>/mnt/data/</c>) and deletes everything on dispose. No mtime tracking,
/// no sidecar, no cross-run reuse — sandbox isolation by design.
/// </summary>
public sealed class SandboxFileUploader : IAsyncDisposable
{
    private readonly OpenAIFileClient _fileClient;
    private readonly ILogger _logger;
    private readonly List<string> _uploadedFileIds = new();
    private bool _disposed;

    public SandboxFileUploader(OpenAIFileClient fileClient, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileClient);
        _fileClient = fileClient;
        _logger = logger ?? LogManager.GetLogger(nameof(SandboxFileUploader));
    }

    /// <summary>
    /// Uploads each local path with <c>purpose=Assistants</c>. Returns a map
    /// <c>basename → fileId</c> (basename without folder). Caller composes
    /// sandbox paths as <c>/mnt/data/{fileId}-{basename}</c>.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> UploadAsync(
        IEnumerable<string> localPaths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(localPaths);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in localPaths)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"sandbox upload missing local file: {path}", path);

            var basename = Path.GetFileName(path);
            await using var stream = File.OpenRead(path);
            var info = await _fileClient.UploadFileAsync(
                file: stream,
                filename: basename,
                purpose: FileUploadPurpose.Assistants,
                cancellationToken: ct);

            _logger.Info("Sandbox uploaded {Basename} → fileId={FileId} ({Size}B)",
                basename, info.Value.Id, new FileInfo(path).Length);
            _uploadedFileIds.Add(info.Value.Id);
            result[basename] = info.Value.Id;
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach (var fileId in _uploadedFileIds)
        {
            try
            {
                await _fileClient.DeleteFileAsync(fileId);
                _logger.Info("Sandbox deleted fileId={FileId}", fileId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Sandbox cleanup failed for fileId={FileId} — leaving for service-side cleanup", fileId);
            }
        }
    }
}
