using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using Autofac;
using DevExpress.Mvvm;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Pipeline;
using FikaFinans.Wpf.Interop;
using FikaFinans.Wpf.ViewModels.Steps;
using FikaFinans.Wpf.Views;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger? _logger;
    private readonly IScheduler? _uiScheduler;
    private readonly ILifetimeScope? _scope;
    private readonly CompositeDisposable _disposables = new();
    private IPipelineRunner? _runner;
    private IReadOnlyDictionary<int, StepViewModel>? _stepsByNumber;

    /// <summary>
    /// Tab index of the NAV Sync tab — placed last (after Step 10) so the
    /// step-number → tab-index mapping in <see cref="OnStepEvent"/> stays
    /// 1:1. Bank=0, Steps 1–10 = 1–10, NAV Sync = 11.
    /// </summary>
    private const int NavSyncTabIndex = 11;

    private string _title = string.Empty;
    private string _selectedWeek = string.Empty;
    private string _selectedFamily = string.Empty;
    private string _runId = "—";
    private string _runStatusText = "Idle";
    private string _statusBarText = "Ready";
    private bool _isRunning;
    private int _selectedTabIndex;
    private CancellationTokenSource _runCts = new();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value, nameof(Title));
    }

    public string SelectedWeek
    {
        get => _selectedWeek;
        set { SetProperty(ref _selectedWeek, value, nameof(SelectedWeek)); PushContextToAllSteps(); }
    }

    public string SelectedFamily
    {
        get => _selectedFamily;
        set { SetProperty(ref _selectedFamily, value, nameof(SelectedFamily)); PushContextToAllSteps(); }
    }

    public string RunId
    {
        get => _runId;
        set => SetProperty(ref _runId, value, nameof(RunId));
    }

    public string RunStatusText
    {
        get => _runStatusText;
        set => SetProperty(ref _runStatusText, value, nameof(RunStatusText));
    }

    public string StatusBarText
    {
        get => _statusBarText;
        set => SetProperty(ref _statusBarText, value, nameof(StatusBarText));
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            SetProperty(ref _isRunning, value, nameof(IsRunning));
            // Mirror into the NAV Sync tab so its run button disables mid-run.
            if (NavSyncTab is not null) NavSyncTab.IsRunning = value;
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            SetProperty(ref _selectedTabIndex, value, nameof(SelectedTabIndex));
            // Load-on-first-open: populate the NAV Sync grid the first time it's
            // shown; thereafter the user reloads with the Refresh button.
            if (value == NavSyncTabIndex
                && NavSyncTab is { HasLoaded: false } tab
                && tab.RefreshCommand.CanExecute(null))
            {
                tab.RefreshCommand.Execute(null);
            }
        }
    }

    public ObservableCollection<string> AvailableWeeks { get; } = new();
    public ObservableCollection<string> AvailableFamilies { get; } = new();

    // ── Tab ViewModels ────────────────────────────────────────────────
    public BankViewModel? BankTab { get; private set; }
    public Step1DataLoaderViewModel? Step1Tab { get; private set; }
    public Step2MetricsCalculatorViewModel? Step2Tab { get; private set; }
    public Step3MacroAnalystViewModel? Step3Tab { get; private set; }
    public Step4SignalScorerViewModel? Step4Tab { get; private set; }
    public Step5MacroAlignerViewModel? Step5Tab { get; private set; }
    public Step6CatalystTaggerViewModel? Step6Tab { get; private set; }
    public Step7ThesisValidatorViewModel? Step7Tab { get; private set; }
    public Step8RecommenderViewModel? Step8Tab { get; private set; }
    public Step9UniverseEnricherViewModel? Step9Tab { get; private set; }
    public Step10PortfolioConstructorViewModel? Step10Tab { get; private set; }
    public NavSyncViewModel? NavSyncTab { get; private set; }

    public ICommand LoadedCommand { get; }
    public ICommand WindowClosingCommand { get; }
    public ICommand RunAllCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    /// <summary>Runtime constructor (DI).</summary>
    public MainWindowViewModel(ILogger logger, IScheduler uiScheduler, ILifetimeScope scope) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    /// <summary>Designer constructor — required for d:DataContext IsDesignTimeCreatable=True.</summary>
    public MainWindowViewModel()
    {
        LoadedCommand = new DelegateCommand(OnLoaded);
        WindowClosingCommand = new DelegateCommand(Dispose);
        RunAllCommand = new AsyncCommand(OnRunAllAsync, () => !IsRunning);
        StopCommand = new DelegateCommand(OnStop, () => IsRunning);
        OpenSettingsCommand = new DelegateCommand(OnOpenSettings);
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        Title = "FikaFinans (Design)";
        AvailableWeeks.Add("2026-W18");
        AvailableFamilies.Add("schroder");
        SelectedWeek = "2026-W18";
        SelectedFamily = "schroder";
        RunId = "20260504-0000";
        RunStatusText = "Idle";
        StatusBarText = "Ready · DB InMemory";
    }

    protected override void OnInitializeInRuntime()
    {
        base.OnInitializeInRuntime();
        Title = "FikaFinans";

        AvailableWeeks.Add("2026-W18");
        AvailableWeeks.Add("2026-W17");
        AvailableWeeks.Add("2026-W16");
        SelectedWeek = AvailableWeeks[0];

        AvailableFamilies.Add("schroder");
        SelectedFamily = AvailableFamilies[0];

        StatusBarText = "Ready · DB InMemory";
    }

    private void OnLoaded()
    {
        _logger?.Info("MainWindowViewModel loaded");

        if (_scope is null) return;

        BankTab = _scope.Resolve<BankViewModel>();
        Step1Tab = _scope.Resolve<Step1DataLoaderViewModel>();
        Step2Tab = _scope.Resolve<Step2MetricsCalculatorViewModel>();
        Step3Tab = _scope.Resolve<Step3MacroAnalystViewModel>();
        Step4Tab = _scope.Resolve<Step4SignalScorerViewModel>();
        Step5Tab = _scope.Resolve<Step5MacroAlignerViewModel>();
        Step6Tab = _scope.Resolve<Step6CatalystTaggerViewModel>();
        Step7Tab = _scope.Resolve<Step7ThesisValidatorViewModel>();
        Step8Tab = _scope.Resolve<Step8RecommenderViewModel>();
        Step9Tab = _scope.Resolve<Step9UniverseEnricherViewModel>();
        Step10Tab = _scope.Resolve<Step10PortfolioConstructorViewModel>();
        NavSyncTab = _scope.Resolve<NavSyncViewModel>();
        NavSyncTab.IsRunning = IsRunning;

        RaisePropertyChanged(nameof(BankTab));
        RaisePropertyChanged(nameof(Step1Tab));
        RaisePropertyChanged(nameof(Step2Tab));
        RaisePropertyChanged(nameof(Step3Tab));
        RaisePropertyChanged(nameof(Step4Tab));
        RaisePropertyChanged(nameof(Step5Tab));
        RaisePropertyChanged(nameof(Step6Tab));
        RaisePropertyChanged(nameof(Step7Tab));
        RaisePropertyChanged(nameof(Step8Tab));
        RaisePropertyChanged(nameof(Step9Tab));
        RaisePropertyChanged(nameof(Step10Tab));
        RaisePropertyChanged(nameof(NavSyncTab));

        // Ensure step VMs have current week/family from the moment they're resolved.
        PushContextToAllSteps();

        // Cache the step-number → VM lookup once VMs are resolved, then subscribe
        // to the orchestrator's event stream. UI scheduler ensures property
        // changes raise on the dispatcher thread.
        _stepsByNumber = new Dictionary<int, StepViewModel>
        {
            [1] = Step1Tab!, [2] = Step2Tab!, [3] = Step3Tab!,  [4] = Step4Tab!,  [5] = Step5Tab!,
            [6] = Step6Tab!, [7] = Step7Tab!, [8] = Step8Tab!,  [9] = Step9Tab!,  [10] = Step10Tab!,
        };
        _runner = _scope.Resolve<IPipelineRunner>();
        var sub = _runner.Events.ObserveOn(_uiScheduler!).Subscribe(OnStepEvent);
        _disposables.Add(sub);

        // NAV-change front door: the NAV Sync tab publishes signals through the
        // detector; here we (the local equivalent of the Azure queue trigger)
        // collect a batch and kick off a scoped pipeline run for just those
        // ISINs. Buffer coalesces the per-signal pushes into one run.
        var navSource = _scope.Resolve<INavSignalSource>();
        var navSub = navSource.Signals
            .Buffer(TimeSpan.FromMilliseconds(250))
            .Where(batch => batch.Count > 0)
            .ObserveOn(_uiScheduler!)
            .Subscribe(OnNavSignalsBatch);
        _disposables.Add(navSub);
    }

    private void PushContextToAllSteps()
    {
        var steps = new StepViewModel?[]
        {
            Step1Tab, Step2Tab, Step3Tab, Step4Tab, Step5Tab,
            Step6Tab, Step7Tab, Step8Tab, Step9Tab, Step10Tab
        };
        foreach (var vm in steps.OfType<StepViewModel>())
            vm.SetContext(SelectedFamily, SelectedWeek, new PipelineRunId(RunId));
    }

    /// <summary>
    /// Manual "Run all" — the whole universe (no NAV-change scoping).
    /// </summary>
    private Task OnRunAllAsync() => RunPipelineAsync(navDateByIsin: null);

    /// <summary>
    /// Collects a batch of NAV-change signals (raised by the NAV Sync tab) and
    /// kicks off a pipeline run scoped to just those ISINs — the local stand-in
    /// for the Azure queue trigger. Ignored if a run is already in flight.
    /// Runs on the UI scheduler (see the <c>ObserveOn</c> in <see cref="OnLoaded"/>).
    /// </summary>
    private void OnNavSignalsBatch(IList<NavChangeSignal> batch)
    {
        if (_runner is null) return;
        if (IsRunning)
        {
            _logger?.Warn("NAV signals arrived while a run is in progress — ignoring {Count} signal(s)", batch.Count);
            return;
        }

        // One date per ISIN (newest wins if the batch carried duplicates).
        var navDateByIsin = batch
            .GroupBy(s => s.Isin.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(s => s.NavDate), StringComparer.Ordinal);

        _logger?.Info("NAV signals → scoped run for {Count} ISIN(s)", navDateByIsin.Count);
        _ = RunPipelineAsync(navDateByIsin);
    }

    /// <summary>
    /// Runs the streaming pipeline. When <paramref name="navDateByIsin"/> is
    /// supplied, the run is scoped to those ISINs and stamps their NAV dates;
    /// when null, the full universe runs.
    /// </summary>
    private async Task RunPipelineAsync(IReadOnlyDictionary<string, DateTimeOffset>? navDateByIsin)
    {
        if (_runner is null)
        {
            _logger?.Warn("Run invoked before pipeline runner was resolved");
            return;
        }

        // Cancel any previously running chain and issue a fresh token.
        _runCts.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        var scope = navDateByIsin is { Count: > 0 } ? $"{navDateByIsin.Count} changed ISIN(s)" : "all funds";
        IsRunning = true;
        RunId = DateTime.Now.ToString("yyyyMMdd-HHmm");
        RunStatusText = "Running…";
        StatusBarText = $"Run {RunId} started ({scope})";
        _logger?.Info("Run started: {RunId} ({Scope})", RunId, scope);

        var steps = new StepViewModel?[]
        {
            Step1Tab, Step2Tab, Step3Tab, Step4Tab, Step5Tab,
            Step6Tab, Step7Tab, Step8Tab, Step9Tab, Step10Tab
        };

        // Push week / family / runId so each VM can resolve its output path
        // when LoadOutputAsync is called from the event handler.
        foreach (var vm in steps.OfType<StepViewModel>())
        {
            vm.SetContext(SelectedFamily, SelectedWeek, new PipelineRunId(RunId));
            vm.Status = StepStatus.Pending;
            vm.HasError = false;
            vm.ErrorText = string.Empty;
            vm.PerFundProcessed = 0;
            vm.PerFundTotal = 0;
        }

        bool allOk;
        try
        {
            allOk = await _runner.RunAllStreamingAsync(
                SelectedFamily, SelectedWeek, new PipelineRunId(RunId), navDateByIsin: navDateByIsin, ct: ct);
        }
        catch (OperationCanceledException)
        {
            allOk = false;
        }

        IsRunning = false;

        var completedSteps = steps.OfType<StepViewModel>().Count(v => v.Status == StepStatus.Ok);
        var errorStep = steps.OfType<StepViewModel>().FirstOrDefault(v => v.Status == StepStatus.Error);

        if (ct.IsCancellationRequested)
        {
            RunStatusText = "Stopped";
            StatusBarText = $"Run {RunId} stopped by user";
        }
        else if (errorStep is not null)
        {
            RunStatusText = $"Error at step {errorStep.StepNumber}";
            StatusBarText = $"Run {RunId} failed at step {errorStep.StepNumber}";
        }
        else if (allOk)
        {
            RunStatusText = $"Done — {completedSteps}/10 steps ok";
            StatusBarText = $"Run {RunId} completed";
        }

        _logger?.Info("Run all finished: RunId={RunId} Steps={Steps}", RunId, completedSteps);
    }

    /// <summary>
    /// Routes a <see cref="StepEvent"/> from <see cref="IPipelineRunner"/> to
    /// the corresponding <see cref="StepViewModel"/>. Runs on the UI scheduler
    /// per the <c>ObserveOn</c> in <see cref="OnLoaded"/>, so direct property
    /// writes here are safe.
    /// </summary>
    private void OnStepEvent(StepEvent evt)
    {
        if (_stepsByNumber is null) return;
        if (!_stepsByNumber.TryGetValue(evt.Step.Value, out var vm)) return;

        // Per-fund ticks (Isin populated) drive the progress counter on the
        // six per-ISIN step VMs without flipping the universe-wide status.
        if (evt.Isin is not null)
        {
            if (evt.Kind == StepEventKind.Succeeded)
                vm.PerFundProcessed++;
            return;
        }

        switch (evt.Kind)
        {
            case StepEventKind.Started:
                vm.Status = StepStatus.Running;
                vm.IsRunning = true;
                vm.HasError = false;
                vm.ErrorText = string.Empty;
                if (evt.Total is { } total)
                {
                    vm.PerFundTotal = total;
                    vm.PerFundProcessed = 0;
                }
                SelectedTabIndex = vm.StepNumber;
                RunStatusText = $"Step {vm.StepNumber}/10…";
                break;

            case StepEventKind.Succeeded:
                vm.Status = StepStatus.Ok;
                vm.IsRunning = false;
                vm.LastRunText = DateTime.Now.ToString("HH:mm:ss");
                if (evt.Duration is { } dur)
                    vm.DurationText = $"{dur.TotalSeconds:N1} s";
                // Fire-and-forget the output refresh; failures are logged but
                // don't block the next step's event from being processed.
                _ = vm.LoadOutputAsync().ContinueWith(
                    t => _logger?.Error(t.Exception, "LoadOutputAsync failed for {Step}", evt.Step),
                    TaskContinuationOptions.OnlyOnFaulted);
                break;

            case StepEventKind.Failed:
                vm.Status = StepStatus.Error;
                vm.IsRunning = false;
                vm.HasError = true;
                vm.ErrorText = evt.Message ?? "unknown error";
                if (evt.Duration is { } failDur)
                    vm.DurationText = $"{failDur.TotalSeconds:N1} s";
                break;
        }
    }

    private void OnStop()
    {
        _runCts.Cancel();
        IsRunning = false;
        RunStatusText = "Stopped";
        StatusBarText = $"Run {RunId} stopped by user";
        _logger?.Info("Run stopped by user");
    }

    private void OnOpenSettings()
    {
        if (_scope is null) return;
        try
        {
            var dialog = _scope.Resolve<SettingsWindow>();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open settings dialog");
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();

        // Dispose tab VMs that are IDisposable
        BankTab?.Dispose();
        Step1Tab?.Dispose();
        Step2Tab?.Dispose();
        Step3Tab?.Dispose();
        Step4Tab?.Dispose();
        Step5Tab?.Dispose();
        Step6Tab?.Dispose();
        Step7Tab?.Dispose();
        Step8Tab?.Dispose();
        Step9Tab?.Dispose();
        Step10Tab?.Dispose();
    }
}
