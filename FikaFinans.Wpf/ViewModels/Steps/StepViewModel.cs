using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public enum StepStatus { Pending, Running, Ok, Error }

/// <summary>Base class for all 10 pipeline step view models.</summary>
public abstract class StepViewModel : ViewModelBase, IDisposable
{
    protected readonly ILogger? Logger;
    protected readonly IScheduler? UiScheduler;
    protected readonly CompositeDisposable Disposables = new();
    protected IConfigEditorDialogService? _configEditorService;

    private StepStatus _status = StepStatus.Pending;
    private string _lastRunText = "Not run";
    private string _durationText = string.Empty;
    private string _outputSummaryText = string.Empty;
    private string _errorText = string.Empty;
    private bool _hasError;
    private bool _isRunning;
    private string _outputJson = string.Empty;
    private bool _errorsExpanded;
    private int _perFundProcessed;
    private int _perFundTotal;

    public abstract int StepNumber { get; }
    public abstract string AgentName { get; }
    public abstract bool HasConfig { get; }
    public virtual bool HasBank => false;
    public virtual ICommand? SendToBankCommand => null;

    public string HeaderText => $"Step {StepNumber} · {AgentName}";

    public string StatusPip => Status switch
    {
        StepStatus.Pending => "·",
        StepStatus.Running => "⟳",
        StepStatus.Ok => "✓",
        StepStatus.Error => "!",
        _ => "·"
    };

    public StepStatus Status
    {
        get => _status;
        set
        {
            SetProperty(ref _status, value, nameof(Status));
            RaisePropertyChanged(nameof(StatusPip));
        }
    }

    public string LastRunText
    {
        get => _lastRunText;
        set => SetProperty(ref _lastRunText, value, nameof(LastRunText));
    }

    public string DurationText
    {
        get => _durationText;
        set => SetProperty(ref _durationText, value, nameof(DurationText));
    }

    public string OutputSummaryText
    {
        get => _outputSummaryText;
        set => SetProperty(ref _outputSummaryText, value, nameof(OutputSummaryText));
    }

    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value, nameof(ErrorText));
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value, nameof(HasError));
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value, nameof(IsRunning));
    }

    public string OutputJson
    {
        get => _outputJson;
        set => SetProperty(ref _outputJson, value, nameof(OutputJson));
    }

    public bool ErrorsExpanded
    {
        get => _errorsExpanded;
        set => SetProperty(ref _errorsExpanded, value, nameof(ErrorsExpanded));
    }

    /// <summary>
    /// Per-fund progress count for per-ISIN steps (2, 4, 5, 6, 7, 8) during a
    /// streaming run. Incremented on every per-fund <c>Succeeded</c> event;
    /// reset to 0 when the universe-wide <c>Started</c> event for this step
    /// arrives.
    /// </summary>
    public int PerFundProcessed
    {
        get => _perFundProcessed;
        set
        {
            SetProperty(ref _perFundProcessed, value, nameof(PerFundProcessed));
            RaisePropertyChanged(nameof(PerFundProgressText));
        }
    }

    /// <summary>
    /// Universe size for the streaming run. Set from the universe-wide
    /// <c>Started</c> event's <c>Total</c> field at the start of the per-ISIN
    /// block. Zero outside a streaming run.
    /// </summary>
    public int PerFundTotal
    {
        get => _perFundTotal;
        set
        {
            SetProperty(ref _perFundTotal, value, nameof(PerFundTotal));
            RaisePropertyChanged(nameof(PerFundProgressText));
        }
    }

    /// <summary>"137 / 1500" once a streaming run sets the total; empty otherwise.</summary>
    public string PerFundProgressText => _perFundTotal > 0
        ? $"{_perFundProcessed} / {_perFundTotal}"
        : string.Empty;

    // ── Run context (set by MainWindowViewModel before each run) ──────────
    public string Family  { get; set; } = string.Empty;
    public string IsoWeek { get; set; } = string.Empty;
    public string RunId   { get; set; } = string.Empty;

    /// <summary>Pushes week/family/runId context before triggering Run step or Run all.</summary>
    public void SetContext(string family, string isoWeek, string runId)
    {
        Family  = family;
        IsoWeek = isoWeek;
        RunId   = runId;
    }

    public ICommand RunStepCommand { get; }
    public ICommand EditConfigCommand { get; }

    protected StepViewModel()
    {
        RunStepCommand = new AsyncCommand(ExecuteRunStepAsync, () => !IsRunning);
        EditConfigCommand = new DelegateCommand(ExecuteEditConfig, () => HasConfig);
    }

    protected StepViewModel(ILogger logger, IScheduler uiScheduler) : this()
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        UiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
    }

    private async Task ExecuteRunStepAsync()
    {
        IsRunning = true;
        Status = StepStatus.Running;
        HasError = false;
        ErrorText = string.Empty;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await RunStepCoreAsync();
            sw.Stop();
            Status = StepStatus.Ok;
            LastRunText = DateTime.Now.ToString("HH:mm:ss");
            DurationText = $"{sw.Elapsed.TotalSeconds:N1} s";
        }
        catch (Exception ex)
        {
            sw.Stop();
            Status = StepStatus.Error;
            HasError = true;
            ErrorText = ex.Message;
            Logger?.Error(ex, "Step {StepNumber} failed", StepNumber);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ExecuteEditConfig()
    {
        var path = GetConfigPath();
        if (string.IsNullOrEmpty(path))
        {
            Logger?.Warn("No config path for step {StepNumber}", StepNumber);
            return;
        }
        _configEditorService?.Edit(path);
    }

    protected virtual string? GetConfigPath() => null;

    protected abstract Task RunStepCoreAsync();

    /// <summary>
    /// Refresh <see cref="OutputJson"/> and <see cref="OutputSummaryText"/> from
    /// the step's persisted output file. Called by
    /// <see cref="FikaFinans.Wpf.ViewModels.MainWindowViewModel"/> after the
    /// orchestrator reports the step finished — the agent has already written
    /// its file, the VM just re-reads it for display. Base implementation is a
    /// no-op; each step VM overrides with its own deserialize + summary logic.
    /// </summary>
    public virtual Task LoadOutputAsync() => Task.CompletedTask;

    /// <summary>
    /// Called by <see cref="FikaFinans.Wpf.ViewModels.MainWindowViewModel"/> to await
    /// each step during a "Run all" chain without going through the ICommand layer.
    /// </summary>
    public Task RunStepAsync() => ExecuteRunStepAsync();

    public void Dispose() => Disposables.Dispose();
}
