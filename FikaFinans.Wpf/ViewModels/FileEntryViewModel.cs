using System.Globalization;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Agents;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// One row of the upload-status strip. State source is the latest
/// <see cref="FoundryFileEntry"/> reported by the file store. Surfaces both
/// local-disk facts (mtime/size) and Foundry-side facts (uploaded mtime/size,
/// fileId) so the user can see *why* a row is Stale.
/// </summary>
public sealed class FileEntryViewModel : ViewModelBase
{
    private string _logicalName = string.Empty;
    private string _localPath = string.Empty;
    private bool _localExists;
    private FoundryFileStatus _status;
    private DateTimeOffset? _localMtime;
    private long? _localSize;
    private DateTimeOffset? _uploadedAt;
    private DateTimeOffset? _uploadedSourceMtime;
    private long? _uploadedSourceSize;
    private string? _uploadedFileId;
    private bool _isBusy;

    public FileEntryViewModel()
    {
        RefreshThisCommand = new DelegateCommand(() => RefreshRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
    }

    public FileEntryViewModel(FoundryFileEntry entry) : this()
    {
        ApplyEntry(entry);
    }

    public string LogicalName
    {
        get => _logicalName;
        set
        {
            if (SetProperty(ref _logicalName, value, nameof(LogicalName)))
                RaisePropertyChanged(nameof(Description));
        }
    }

    /// <summary>One-line hint describing what this file contains, looked up by logical name.</summary>
    public string Description =>
        FundDataFiles.Descriptions.TryGetValue(LogicalName, out var d) ? d : string.Empty;

    public string LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value, nameof(LocalPath));
    }

    public bool LocalExists
    {
        get => _localExists;
        set
        {
            if (SetProperty(ref _localExists, value, nameof(LocalExists)))
                RaisePropertyChanged(nameof(LocalSummaryLabel));
        }
    }

    public FoundryFileStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value, nameof(Status)))
            {
                RaisePropertyChanged(nameof(StatusLabel));
                RaisePropertyChanged(nameof(IsAttention));
            }
        }
    }

    public DateTimeOffset? LocalMtime
    {
        get => _localMtime;
        set
        {
            if (SetProperty(ref _localMtime, value, nameof(LocalMtime)))
                RaisePropertyChanged(nameof(LocalSummaryLabel));
        }
    }

    public long? LocalSize
    {
        get => _localSize;
        set
        {
            if (SetProperty(ref _localSize, value, nameof(LocalSize)))
                RaisePropertyChanged(nameof(LocalSummaryLabel));
        }
    }

    public DateTimeOffset? UploadedAt
    {
        get => _uploadedAt;
        set
        {
            if (SetProperty(ref _uploadedAt, value, nameof(UploadedAt)))
                RaisePropertyChanged(nameof(FoundrySummaryLabel));
        }
    }

    public DateTimeOffset? UploadedSourceMtime
    {
        get => _uploadedSourceMtime;
        set
        {
            if (SetProperty(ref _uploadedSourceMtime, value, nameof(UploadedSourceMtime)))
                RaisePropertyChanged(nameof(FoundrySummaryLabel));
        }
    }

    public long? UploadedSourceSize
    {
        get => _uploadedSourceSize;
        set
        {
            if (SetProperty(ref _uploadedSourceSize, value, nameof(UploadedSourceSize)))
                RaisePropertyChanged(nameof(FoundrySummaryLabel));
        }
    }

    public string? UploadedFileId
    {
        get => _uploadedFileId;
        set
        {
            if (SetProperty(ref _uploadedFileId, value, nameof(UploadedFileId)))
                RaisePropertyChanged(nameof(UploadedFileIdLabel));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value, nameof(IsBusy)))
                ((DelegateCommand)RefreshThisCommand).RaiseCanExecuteChanged();
        }
    }

    public string StatusLabel => Status switch
    {
        FoundryFileStatus.Missing => "Missing",
        FoundryFileStatus.NotUploaded => "Not uploaded",
        FoundryFileStatus.Stale => "Stale",
        FoundryFileStatus.Fresh => "Fresh",
        _ => Status.ToString(),
    };

    public bool IsAttention =>
        Status is FoundryFileStatus.Missing or FoundryFileStatus.Stale or FoundryFileStatus.NotUploaded;

    /// <summary>e.g. <c>"1.2 KB   modified 2026-04-26 16:32"</c> (local time).
    /// When the file isn't on disk, points at the missing path so the user knows where to drop it.</summary>
    public string LocalSummaryLabel
    {
        get
        {
            if (!LocalExists)
                return string.IsNullOrEmpty(LocalPath) ? "—" : $"—  (file not found at {LocalPath})";
            return $"{FormatSize(LocalSize)}   modified {FormatLocalTime(LocalMtime)}";
        }
    }

    /// <summary>e.g. <c>"1.2 KB   uploaded 2026-04-26 16:32"</c> (local time, from sidecar).</summary>
    public string FoundrySummaryLabel
    {
        get
        {
            if (UploadedAt is null) return "—";
            return $"{FormatSize(UploadedSourceSize)}   uploaded {FormatLocalTime(UploadedAt)}";
        }
    }

    /// <summary>Tooltip text on the status pill — full Foundry fileId or "(not uploaded)".</summary>
    public string UploadedFileIdLabel =>
        string.IsNullOrEmpty(UploadedFileId) ? "(not uploaded)" : UploadedFileId;

    public ICommand RefreshThisCommand { get; }

    /// <summary>Raised when the user clicks the per-row ↻ button.</summary>
    public event EventHandler? RefreshRequested;

    public void ApplyEntry(FoundryFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LogicalName = entry.LogicalName;
        LocalPath = entry.LocalPath;
        LocalExists = entry.LocalExists;
        Status = entry.Status;
        LocalMtime = entry.LocalMtime;
        LocalSize = entry.LocalSize;
        UploadedAt = entry.UploadedAt;
        UploadedSourceMtime = entry.UploadedSourceMtime;
        UploadedSourceSize = entry.UploadedSourceSize;
        UploadedFileId = entry.UploadedFileId;
    }

    private static string FormatSize(long? bytes)
    {
        if (bytes is null) return "—";
        var b = bytes.Value;
        if (b < 1024) return $"{b} B";
        if (b < 1024 * 1024) return $"{b / 1024.0:0.#} KB";
        if (b < 1024L * 1024 * 1024) return $"{b / (1024.0 * 1024):0.#} MB";
        return $"{b / (1024.0 * 1024 * 1024):0.##} GB";
    }

    private static string FormatLocalTime(DateTimeOffset? when) =>
        when is null
            ? "—"
            : when.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
