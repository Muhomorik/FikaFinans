using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Windows.Input;
using Autofac;
using DevExpress.Mvvm;
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
        set => SetProperty(ref _isRunning, value, nameof(IsRunning));
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value, nameof(SelectedTabIndex));
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

        // Ensure step VMs have current week/family from the moment they're resolved.
        PushContextToAllSteps();
    }

    private void PushContextToAllSteps()
    {
        var steps = new StepViewModel?[]
        {
            Step1Tab, Step2Tab, Step3Tab, Step4Tab, Step5Tab,
            Step6Tab, Step7Tab, Step8Tab, Step9Tab, Step10Tab
        };
        foreach (var vm in steps.OfType<StepViewModel>())
            vm.SetContext(SelectedFamily, SelectedWeek, RunId);
    }

    private async Task OnRunAllAsync()
    {
        // Cancel any previously running chain and issue a fresh token.
        _runCts.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        IsRunning = true;
        RunId = DateTime.Now.ToString("yyyyMMdd-HHmm");
        RunStatusText = "Running…";
        StatusBarText = $"Run {RunId} started";
        _logger?.Info("Run all started: {RunId}", RunId);

        var steps = new StepViewModel?[]
        {
            Step1Tab, Step2Tab, Step3Tab, Step4Tab, Step5Tab,
            Step6Tab, Step7Tab, Step8Tab, Step9Tab, Step10Tab
        };

        // Push week / family / runId to every step VM before the first runs.
        foreach (var vm in steps.OfType<StepViewModel>())
            vm.SetContext(SelectedFamily, SelectedWeek, RunId);

        // Run steps 1-10 in sequence; halt on first error or cancellation.
        foreach (var vm in steps.OfType<StepViewModel>())
        {
            if (ct.IsCancellationRequested) break;

            SelectedTabIndex = vm.StepNumber; // navigate to the running tab
            RunStatusText = $"Step {vm.StepNumber}/10…";

            await vm.RunStepAsync();

            if (vm.Status == StepStatus.Error) break;
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
        else
        {
            RunStatusText = $"Done — {completedSteps}/10 steps ok";
            StatusBarText = $"Run {RunId} completed";
        }

        _logger?.Info("Run all finished: RunId={RunId} Steps={Steps}", RunId, completedSteps);
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
