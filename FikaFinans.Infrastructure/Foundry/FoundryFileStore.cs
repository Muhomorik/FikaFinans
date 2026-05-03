using System.ClientModel;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using FikaFinans.Application.Agents;
using NLog;
using OpenAI.Files;

namespace FikaFinans.Infrastructure.Foundry;

/// <summary>
/// Foundry-side file cache for the canonical fund-data files. Tracks
/// <c>logicalName → (fileId, sourceMtime, sourceSize, uploadedAt)</c> in a JSON sidecar
/// at <c>%APPDATA%\FikaFinans\foundry-files.json</c>. The sidecar is the source of
/// truth for "what's currently uploaded."
/// </summary>
/// <remarks>
/// Uploads go through the OpenAI Files API (purpose=Assistants) via
/// <see cref="OpenAIFileClient"/> — that's what Code Interpreter mounts at <c>/mnt/data/</c>.
/// The classic <c>PersistentAgentsClient.Files</c> path (purpose=Agents) lands files under
/// the portal's "Datasets" tab as <c>uri_file</c> instead, which Code Interpreter can't read.
/// </remarks>
public sealed class FoundryFileStore : IFoundryFileStore
{
    private const string SidecarFileName = "foundry-files.json";
    private const string AppFolderName = "FikaFinans";

    /// <summary>
    /// Bumped from 1 → 2 when migrating off the classic Persistent Agents SDK. Sidecars
    /// written by the old code are silently discarded so the first run after migration
    /// re-uploads everything via the OpenAI Files API.
    /// </summary>
    private const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger _logger;
    private readonly OpenAIFileClient _fileClient;
    private readonly string _sidecarPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FoundryFileStore(ILogger logger, AIProjectClient projectClient, string? sidecarPath = null)
    {
        _logger = logger;
        _fileClient = projectClient.ProjectOpenAIClient.GetOpenAIFileClient();
        if (sidecarPath is null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _sidecarPath = Path.Combine(appData, AppFolderName, SidecarFileName);
        }
        else
        {
            _sidecarPath = sidecarPath;
        }
    }

    public string SidecarFilePath => _sidecarPath;

    public async Task<IReadOnlyList<FoundryFileEntry>> GetStatusAsync(
        FundDataFileSet fileSet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSet);
        var sidecar = await ReadSidecarAsync(ct);
        return [.. fileSet.EnumerateFiles().Select(f => InspectOne(f.LogicalName, f.LocalPath, sidecar))];
    }

    public async Task<IReadOnlyDictionary<string, string>> EnsureFilesUploadedAsync(
        FundDataFileSet fileSet,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSet);

        await _gate.WaitAsync(ct);
        try
        {
            var sidecar = await ReadSidecarAsync(ct);
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (logicalName, localPath) in fileSet.EnumerateFiles())
            {
                var entry = InspectOne(logicalName, localPath, sidecar);

                if (entry.Status == FoundryFileStatus.Missing)
                {
                    throw new FileNotFoundException(
                        $"missing {logicalName} in {Path.GetDirectoryName(localPath)}", localPath);
                }

                if (entry.Status == FoundryFileStatus.Fresh && entry.UploadedFileId is not null)
                {
                    // Local mtime says we're current, but the user may have deleted the file
                    // server-side via the portal. A 404 here means we re-upload silently
                    // instead of handing Code Interpreter a ghost fileId.
                    if (await ExistsOnServerAsync(entry.UploadedFileId, ct))
                    {
                        resolved[logicalName] = entry.UploadedFileId;
                        continue;
                    }
                    _logger.Info(
                        "Sidecar fileId {FileId} for {LogicalName} absent server-side (manual delete?) — re-uploading",
                        entry.UploadedFileId, logicalName);
                }
                else if (sidecar.Files.TryGetValue(logicalName, out var stale))
                {
                    await TryDeleteFoundryFileAsync(stale.FileId, ct);
                }

                var newId = await UploadOneAsync(logicalName, localPath, ct);
                sidecar.Files[logicalName] = new SidecarRecord(
                    FileId: newId,
                    SourceMtimeUtc: File.GetLastWriteTimeUtc(localPath),
                    SourceSize: new FileInfo(localPath).Length,
                    UploadedAtUtc: DateTimeOffset.UtcNow);
                resolved[logicalName] = newId;
            }

            await WriteSidecarAsync(sidecar, ct);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> ForceReuploadAllAsync(
        FundDataFileSet fileSet,
        IProgress<FoundryFileEntry>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSet);

        await _gate.WaitAsync(ct);
        try
        {
            var sidecar = await ReadSidecarAsync(ct);
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (logicalName, localPath) in fileSet.EnumerateFiles())
            {
                if (!File.Exists(localPath))
                {
                    var missing = InspectOne(logicalName, localPath, sidecar);
                    progress?.Report(missing);
                    throw new FileNotFoundException(
                        $"missing {logicalName} in {Path.GetDirectoryName(localPath)}", localPath);
                }

                if (sidecar.Files.TryGetValue(logicalName, out var stale))
                {
                    await TryDeleteFoundryFileAsync(stale.FileId, ct);
                }

                var newId = await UploadOneAsync(logicalName, localPath, ct);
                sidecar.Files[logicalName] = new SidecarRecord(
                    FileId: newId,
                    SourceMtimeUtc: File.GetLastWriteTimeUtc(localPath),
                    SourceSize: new FileInfo(localPath).Length,
                    UploadedAtUtc: DateTimeOffset.UtcNow);
                resolved[logicalName] = newId;

                progress?.Report(InspectOne(logicalName, localPath, sidecar));
            }

            await WriteSidecarAsync(sidecar, ct);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> UploadOneAsync(string logicalName, string localPath, CancellationToken ct)
    {
        var srcInfo = new FileInfo(localPath);
        _logger.Info("Uploading {LogicalName} from {LocalPath} (size={Size}B, mtime={Mtime:O})",
            logicalName, localPath, srcInfo.Length, srcInfo.LastWriteTimeUtc);

        // The (filePath, purpose) overload doesn't accept a CancellationToken; use the
        // stream overload to keep cancellation working. Filename matters because Code
        // Interpreter mounts files at /mnt/data/<filename> — preserve the basename so
        // the prompt's hardcoded names (summary.csv, metadata.csv, ...) resolve.
        await using var stream = File.OpenRead(localPath);
        var info = await _fileClient.UploadFileAsync(
            file: stream,
            filename: Path.GetFileName(localPath),
            purpose: FileUploadPurpose.Assistants,
            cancellationToken: ct);

        _logger.Info("Uploaded {LogicalName} → fileId={FileId} (size={Size}B, mtime={Mtime:O})",
            logicalName, info.Value.Id, srcInfo.Length, srcInfo.LastWriteTimeUtc);
        return info.Value.Id;
    }

    private async Task<bool> ExistsOnServerAsync(string fileId, CancellationToken ct)
    {
        try
        {
            await _fileClient.GetFileAsync(fileId, ct);
            return true;
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    private async Task TryDeleteFoundryFileAsync(string fileId, CancellationToken ct)
    {
        try
        {
            await _fileClient.DeleteFileAsync(fileId, ct);
            _logger.Info("Deleted stale Foundry fileId={FileId}", fileId);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            // Already gone server-side (manual portal delete, server-side TTL, prior crash mid-cleanup).
            // That's the desired end-state — no warning, just note it and move on.
            _logger.Info("Stale Foundry fileId={FileId} already absent server-side — skipping delete", fileId);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to delete stale Foundry fileId={FileId} — continuing anyway", fileId);
        }
    }

    private FoundryFileEntry InspectOne(string logicalName, string localPath, Sidecar sidecar)
    {
        var exists = File.Exists(localPath);
        DateTimeOffset? mtime = null;
        long? size = null;

        if (exists)
        {
            var info = new FileInfo(localPath);
            mtime = info.LastWriteTimeUtc;
            size = info.Length;
        }

        sidecar.Files.TryGetValue(logicalName, out var record);

        FoundryFileStatus status;
        if (!exists)
        {
            status = FoundryFileStatus.Missing;
        }
        else if (record is null)
        {
            status = FoundryFileStatus.NotUploaded;
        }
        else if (mtime > record.SourceMtimeUtc)
        {
            status = FoundryFileStatus.Stale;
            _logger.Debug(
                "{LogicalName} is Stale: localMtime={LocalMtime:O} > sourceMtime={SourceMtime:O} (fileId={FileId}, localSize={LocalSize}B, sourceSize={SourceSize}B)",
                logicalName, mtime, record.SourceMtimeUtc, record.FileId, size, record.SourceSize);
        }
        else
        {
            status = FoundryFileStatus.Fresh;
        }

        return new FoundryFileEntry(
            LogicalName: logicalName,
            LocalPath: localPath,
            LocalExists: exists,
            LocalMtime: mtime,
            LocalSize: size,
            UploadedFileId: record?.FileId,
            UploadedAt: record?.UploadedAtUtc,
            UploadedSourceMtime: record?.SourceMtimeUtc,
            UploadedSourceSize: record?.SourceSize,
            Status: status);
    }

    private async Task<Sidecar> ReadSidecarAsync(CancellationToken ct)
    {
        if (!File.Exists(_sidecarPath))
            return new Sidecar();

        try
        {
            var json = await File.ReadAllTextAsync(_sidecarPath, ct);
            var loaded = JsonSerializer.Deserialize<Sidecar>(json, JsonOptions);
            if (loaded is null)
                return new Sidecar();

            // Drop sidecars from older code paths — fileIds from the classic Persistent Agents
            // SDK (purpose=Agents) don't resolve under the OpenAI Files API.
            if (loaded.SchemaVersion < CurrentSchemaVersion)
            {
                _logger.Info("Sidecar at {Path} has schemaVersion={Old} (< {Current}) — discarding so files re-upload via OpenAI Files API",
                    _sidecarPath, loaded.SchemaVersion, CurrentSchemaVersion);
                return new Sidecar();
            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to read Foundry sidecar at {Path} — treating as empty", _sidecarPath);
            return new Sidecar();
        }
    }

    private async Task WriteSidecarAsync(Sidecar sidecar, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_sidecarPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(sidecar, JsonOptions);
        await File.WriteAllTextAsync(_sidecarPath, json, ct);
    }

    private sealed class Sidecar
    {
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public Dictionary<string, SidecarRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record SidecarRecord(
        string FileId,
        DateTimeOffset SourceMtimeUtc,
        long SourceSize,
        DateTimeOffset UploadedAtUtc);
}
