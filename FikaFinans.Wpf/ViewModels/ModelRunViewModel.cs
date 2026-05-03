using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Domain.Models;

namespace FikaFinans.Wpf.ViewModels;

public enum ModelRunStatus { Idle, Running, Done, Error }

/// <summary>
/// Per-panel state for one model in the comparison view. Owns its own elapsed-second
/// tick stream so panels animate independently and one model finishing doesn't block
/// the others' counters.
/// </summary>
public sealed class ModelRunViewModel : ViewModelBase
{
    private readonly SerialDisposable _tickSubscription = new();
    private string _modelId = string.Empty;
    private ModelRunStatus _status = ModelRunStatus.Idle;
    private long _elapsedMs;
    private int _inputTokens;
    private int _outputTokens;
    private string _responseText = string.Empty;
    private string _errorText = string.Empty;
    private DateTimeOffset? _startedAtUtc;
    private Func<Task>? _runHandler;

    public ModelRunViewModel()
    {
        CopyResponseCommand = new DelegateCommand(OnCopyResponse, () => !string.IsNullOrEmpty(ResponseText));
        RunCommand = new AsyncCommand(OnRunAsync, () => _runHandler is not null && !IsRunning);
    }

    public ModelRunViewModel(string modelId) : this()
    {
        ModelId = modelId;
    }

    public string ModelId
    {
        get => _modelId;
        set => SetProperty(ref _modelId, value, nameof(ModelId));
    }

    public ModelRunStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value, nameof(Status)))
            {
                RaisePropertyChanged(nameof(IsRunning));
                RaisePropertyChanged(nameof(IsError));
                ((AsyncCommand)RunCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRunning => Status == ModelRunStatus.Running;
    public bool IsError => Status == ModelRunStatus.Error;

    public long ElapsedMs
    {
        get => _elapsedMs;
        private set
        {
            if (SetProperty(ref _elapsedMs, value, nameof(ElapsedMs)))
                RaisePropertyChanged(nameof(ElapsedLabel));
        }
    }

    public string ElapsedLabel
    {
        get
        {
            var total = TimeSpan.FromMilliseconds(ElapsedMs);
            return $"{(int)total.TotalMinutes:00}:{total.Seconds:00} elapsed";
        }
    }

    public int InputTokens
    {
        get => _inputTokens;
        private set => SetProperty(ref _inputTokens, value, nameof(InputTokens));
    }

    public int OutputTokens
    {
        get => _outputTokens;
        private set => SetProperty(ref _outputTokens, value, nameof(OutputTokens));
    }

    public string ResponseText
    {
        get => _responseText;
        private set
        {
            if (SetProperty(ref _responseText, value, nameof(ResponseText)))
                ((DelegateCommand)CopyResponseCommand).RaiseCanExecuteChanged();
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set => SetProperty(ref _errorText, value, nameof(ErrorText));
    }

    public ICommand CopyResponseCommand { get; }
    public ICommand RunCommand { get; }

    /// <summary>
    /// Wires the per-panel "Run this model" button to the parent VM's single-model
    /// pipeline. Called after the panel is created so the handler can close over both
    /// the agent and this panel.
    /// </summary>
    public void ConfigureRun(Func<Task> runHandler)
    {
        _runHandler = runHandler ?? throw new ArgumentNullException(nameof(runHandler));
        ((AsyncCommand)RunCommand).RaiseCanExecuteChanged();
    }

    private async Task OnRunAsync()
    {
        if (_runHandler is null) return;
        await _runHandler();
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        ModelId = "design-model";
        Status = ModelRunStatus.Done;
        ElapsedMs = 67_000;
        InputTokens = 12_345;
        OutputTokens = 678;
        ResponseText = "Design-time placeholder for the model response.\nMultiline.";
    }

    public void StartRunning()
    {
        Status = ModelRunStatus.Running;
        ElapsedMs = 0;
        InputTokens = 0;
        OutputTokens = 0;
        ResponseText = string.Empty;
        ErrorText = string.Empty;
        _startedAtUtc = DateTimeOffset.UtcNow;

        // Tick once a second on the WPF dispatcher so SetProperty stays UI-thread-safe.
        _tickSubscription.Disposable = Observable
            .Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(DispatcherScheduler.Current)
            .Subscribe(_ =>
            {
                if (_startedAtUtc is { } start)
                    ElapsedMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
            });
    }

    public void Complete(FundAnalyticsRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _tickSubscription.Disposable = Disposable.Empty;
        ElapsedMs = run.ElapsedMs;
        InputTokens = run.InputTokens;
        OutputTokens = run.OutputTokens;
        ResponseText = run.ResponseText;
        Status = ModelRunStatus.Done;
    }

    public void Fail(Exception error)
    {
        _tickSubscription.Disposable = Disposable.Empty;
        if (_startedAtUtc is { } start)
            ElapsedMs = (long)(DateTimeOffset.UtcNow - start).TotalMilliseconds;
        ErrorText = error?.Message ?? "Unknown error";
        Status = ModelRunStatus.Error;
    }

    private void OnCopyResponse()
    {
        if (string.IsNullOrEmpty(ResponseText)) return;
        try
        {
            Clipboard.SetText(ResponseText);
        }
        catch
        {
            // Clipboard can transiently fail on Windows when other processes hold it.
            // Silent failure is fine — user can retry.
        }
    }
}
