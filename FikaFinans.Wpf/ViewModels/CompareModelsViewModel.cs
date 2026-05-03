using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Agents;
using FikaFinans.Application.UseCases;
using FikaFinans.Domain.Models;
using FikaFinans.Infrastructure.Foundry;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// Backs the Compare Models tab. Owns the file-status strip, the question entry,
/// and the per-model panels (one per deployed model — currently 2; see
/// <c>Docs/models.md</c>). Per-model failures don't abort sibling runs.
/// </summary>
public sealed class CompareModelsViewModel : ViewModelBase
{
    private readonly IFoundryFileStore? _fileStore;
    private readonly FundDataFileSet? _fileSet;
    private readonly CompareModelsUseCase? _useCase;
    private readonly IReadOnlyList<IFundAnalyticsAgent>? _agents;
    private readonly ILogger? _logger;

    private string _question = string.Empty;
    private bool _isRunning;
    private string _errorMessage = string.Empty;
    private CancellationTokenSource? _runCts;

    public CompareModelsViewModel()
    {
        Files = [];
        Run1 = new ModelRunViewModel(FoundryModelIds.Gpt5_4_1);
        Run2 = new ModelRunViewModel(FoundryModelIds.DeepSeekR1_0528_1);

        // Commands must be assigned before Question — the Question setter raises
        // RunComparisonCommand.CanExecuteChanged, and a null command would NRE.
        RunComparisonCommand = new AsyncCommand(RunComparisonAsync, () => !IsRunning && !string.IsNullOrWhiteSpace(Question));
        RefreshAllFilesCommand = new AsyncCommand(RefreshAllFilesAsync, () => !IsRunning);

        Question = "Sample question — designer preview only.";
    }

    public CompareModelsViewModel(
        IFoundryFileStore fileStore,
        FundDataFileSet fileSet,
        CompareModelsUseCase useCase,
        IEnumerable<IFundAnalyticsAgent> agents,
        IDefaultUserPromptProvider defaultUserPromptProvider,
        ILogger logger) : this()
    {
        ArgumentNullException.ThrowIfNull(defaultUserPromptProvider);

        _fileStore = fileStore ?? throw new ArgumentNullException(nameof(fileStore));
        _fileSet = fileSet ?? throw new ArgumentNullException(nameof(fileSet));
        _useCase = useCase ?? throw new ArgumentNullException(nameof(useCase));
        _agents = agents.ToList();
        _logger = logger;

        Question = defaultUserPromptProvider.GetDefaultUserPrompt();

        // Replace the design-time panels with one per registered model in registration order.
        Run1 = new ModelRunViewModel(_agents.ElementAtOrDefault(0)?.ModelId ?? FoundryModelIds.Gpt5_4_1);
        Run2 = new ModelRunViewModel(_agents.ElementAtOrDefault(1)?.ModelId ?? FoundryModelIds.DeepSeekR1_0528_1);

        if (_agents.ElementAtOrDefault(0) is { } agent1) Run1.ConfigureRun(() => RunSingleAsync(agent1, Run1));
        if (_agents.ElementAtOrDefault(1) is { } agent2) Run2.ConfigureRun(() => RunSingleAsync(agent2, Run2));

        // DevExpressMvvm's OnInitializeInRuntime hook only fires when the framework's
        // ViewModel-init behavior is wired (it isn't here), so kick the initial status
        // load off the ctor — guaranteed to run, deps are already assigned above.
        _ = RefreshFileStatusAsync(CancellationToken.None);
    }

    public ObservableCollection<FileEntryViewModel> Files { get; }

    public string Question
    {
        get => _question;
        set
        {
            if (SetProperty(ref _question, value, nameof(Question)))
                ((AsyncCommand)RunComparisonCommand).RaiseCanExecuteChanged();
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value, nameof(IsRunning)))
            {
                ((AsyncCommand)RunComparisonCommand).RaiseCanExecuteChanged();
                ((AsyncCommand)RefreshAllFilesCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value, nameof(ErrorMessage));
    }

    public ModelRunViewModel Run1 { get; private set; }
    public ModelRunViewModel Run2 { get; private set; }

    public ICommand RunComparisonCommand { get; }
    public ICommand RefreshAllFilesCommand { get; }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        Files.Add(new FileEntryViewModel
        {
            LogicalName = FundDataFiles.Summary,
            LocalPath = @"C:\Users\you\Documents\fund-data\summary.csv",
            LocalExists = true,
            Status = FoundryFileStatus.Fresh,
            LocalMtime = DateTimeOffset.UtcNow.AddMinutes(-10),
            LocalSize = 1234,
            UploadedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            UploadedSourceMtime = DateTimeOffset.UtcNow.AddMinutes(-10),
            UploadedSourceSize = 1234,
            UploadedFileId = "assistant-abc123def456",
        });
        Files.Add(new FileEntryViewModel
        {
            LogicalName = FundDataFiles.Metadata,
            LocalPath = @"C:\Users\you\Documents\fund-data\metadata.csv",
            LocalExists = true,
            Status = FoundryFileStatus.Stale,
            LocalMtime = DateTimeOffset.UtcNow.AddMinutes(-5),
            LocalSize = 2150,
            UploadedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UploadedSourceMtime = DateTimeOffset.UtcNow.AddDays(-1),
            UploadedSourceSize = 2048,
            UploadedFileId = "assistant-xyz789",
        });
        Files.Add(new FileEntryViewModel
        {
            LogicalName = FundDataFiles.Positions,
            LocalPath = @"C:\Users\you\Documents\fund-data\positions.csv",
            LocalExists = true,
            Status = FoundryFileStatus.NotUploaded,
            LocalMtime = DateTimeOffset.UtcNow.AddMinutes(-20),
            LocalSize = 8704,
        });
        Files.Add(new FileEntryViewModel
        {
            LogicalName = FundDataFiles.Structure,
            LocalPath = @"C:\Users\you\Documents\fund-data\portfolio_structure.md",
            LocalExists = false,
            Status = FoundryFileStatus.Missing,
        });
    }

    private async Task RefreshFileStatusAsync(CancellationToken ct)
    {
        if (_fileStore is null || _fileSet is null) return;
        try
        {
            var entries = await _fileStore.GetStatusAsync(_fileSet, ct);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => RebuildFiles(entries));
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to load file status");
            ErrorMessage = $"Failed to load file status: {ex.Message}";
        }
    }

    private void RebuildFiles(IReadOnlyList<FoundryFileEntry> entries)
    {
        // Map by logical name; create rows on first visit, otherwise update in-place
        // so existing event subscriptions and UI bindings don't churn.
        var byName = Files.ToDictionary(f => f.LogicalName, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (byName.TryGetValue(entry.LogicalName, out var existing))
            {
                existing.ApplyEntry(entry);
            }
            else
            {
                var vm = new FileEntryViewModel(entry);
                vm.RefreshRequested += OnRowRefreshRequested;
                Files.Add(vm);
            }
        }
    }

    private async void OnRowRefreshRequested(object? sender, EventArgs e)
    {
        if (sender is not FileEntryViewModel row || _fileStore is null || _fileSet is null) return;
        try
        {
            row.IsBusy = true;
            // Per-row refresh = full ForceReuploadAll for now; the store dedupes and the operation
            // is bounded to a handful of small files. Per-row partial-reupload can land later if needed.
            await _fileStore.ForceReuploadAllAsync(_fileSet, progress: null, CancellationToken.None);
            await RefreshFileStatusAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Per-row refresh failed for {LogicalName}", row.LogicalName);
            ErrorMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    private async Task RefreshAllFilesAsync()
    {
        if (_fileStore is null || _fileSet is null) return;
        ErrorMessage = string.Empty;
        try
        {
            IsRunning = true;
            var progress = new Progress<FoundryFileEntry>(entry =>
            {
                var existing = Files.FirstOrDefault(f =>
                    string.Equals(f.LogicalName, entry.LogicalName, StringComparison.OrdinalIgnoreCase));
                existing?.ApplyEntry(entry);
            });
            await _fileStore.ForceReuploadAllAsync(_fileSet, progress, CancellationToken.None);
            await RefreshFileStatusAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Refresh-all failed");
            ErrorMessage = $"Refresh failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunComparisonAsync()
    {
        if (_useCase is null || _agents is null || _fileStore is null || _fileSet is null) return;

        ErrorMessage = string.Empty;
        IsRunning = true;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        try
        {
            // Implicit upload — the user can drop new CSVs in the folder and just hit Run.
            await _fileStore.EnsureFilesUploadedAsync(_fileSet, ct);
            await RefreshFileStatusAsync(ct);

            var panels = new[] { Run1, Run2 };
            var pairs = _agents.Zip(panels, (agent, panel) => (agent, panel)).ToArray();

            foreach (var (_, panel) in pairs)
                panel.StartRunning();

            // Fan out one task per model — each panel finishes (or fails) on its own without
            // blocking siblings; the panels update themselves inside RunOneAsync.
            var tasks = pairs.Select(p => RunOneAsync(p.agent, p.panel, Question, ct)).ToArray();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Comparison failed");
            ErrorMessage = $"Comparison failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunOneAsync(IFundAnalyticsAgent agent, ModelRunViewModel panel, string question, CancellationToken ct)
    {
        try
        {
            var run = await agent.RunAsync(question, ct);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => panel.Complete(run));
        }
        catch (Exception ex)
        {
            _logger?.Warn(ex, "Run failed for model {ModelId}", agent.ModelId);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => panel.Fail(ex));
        }
    }

    private async Task RunSingleAsync(IFundAnalyticsAgent agent, ModelRunViewModel panel)
    {
        if (_fileStore is null || _fileSet is null) return;
        if (string.IsNullOrWhiteSpace(Question)) return;
        if (panel.IsRunning) return;

        ErrorMessage = string.Empty;
        IsRunning = true;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        try
        {
            await _fileStore.EnsureFilesUploadedAsync(_fileSet, ct);
            await RefreshFileStatusAsync(ct);

            panel.StartRunning();
            await RunOneAsync(agent, panel, Question, ct);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Single-model run failed for {ModelId}", agent.ModelId);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => panel.Fail(ex));
        }
        finally
        {
            IsRunning = false;
        }
    }
}
