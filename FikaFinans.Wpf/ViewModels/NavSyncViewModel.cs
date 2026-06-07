using System.Collections.ObjectModel;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Pipeline.Signals;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// View model for the NAV Sync tab — the local stand-in for the Azure Queue
/// Storage front door. Shows the company-filtered fund universe with each fund's
/// latest YR NAV date vs its <c>IsinProgressEntity</c> anchor (colour-coded by
/// <see cref="NavSyncStatusKind"/>), and lets the user raise NAV-change signals.
/// </summary>
/// <remarks>
/// The <see cref="CheckAndRunCommand"/> only <em>publishes</em> signals through
/// the detector; <c>MainWindowViewModel</c>'s Rx subscription is what actually
/// triggers the scoped pipeline run — exactly how the Azure queue trigger will
/// fire the Function. Grid loads on first tab open (driven by the parent) and
/// thereafter only via <see cref="RefreshCommand"/>.
/// </remarks>
public sealed class NavSyncViewModel : ViewModelBase
{
    private readonly ILogger? _logger;
    private readonly NavChangeDetector? _detector;
    private readonly NavSyncOptions? _options;

    private string _companyLabel = "All companies";
    private string _yrDbStatusText = "not set";
    private string _summaryText = "Not loaded yet — press Refresh.";
    private bool _isBusy;
    private bool _isRunning;
    private bool _hasLoaded;

    /// <summary>The configured company filter, or "All companies" when blank.</summary>
    public string CompanyLabel
    {
        get => _companyLabel;
        set => SetProperty(ref _companyLabel, value, nameof(CompanyLabel));
    }

    /// <summary>Human-readable YR-DB-configured indicator ("configured" / "not set").</summary>
    public string YrDbStatusText
    {
        get => _yrDbStatusText;
        set => SetProperty(ref _yrDbStatusText, value, nameof(YrDbStatusText));
    }

    /// <summary>"N of M funds have new NAV data" summary above the grid.</summary>
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value, nameof(SummaryText));
    }

    /// <summary>True while a refresh / publish is in flight (disables the buttons).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value, nameof(IsBusy));
    }

    /// <summary>
    /// Mirrors the parent's pipeline-running state so the run button is disabled
    /// while a scoped run is already underway. Set by <c>MainWindowViewModel</c>.
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value, nameof(IsRunning));
    }

    /// <summary>True once the grid has loaded at least once (drives load-on-first-open).</summary>
    public bool HasLoaded
    {
        get => _hasLoaded;
        private set => SetProperty(ref _hasLoaded, value, nameof(HasLoaded));
    }

    /// <summary>The company-filtered status rows shown in the grid.</summary>
    public ObservableCollection<NavSyncStatusRowViewModel> Rows { get; } = new();

    /// <summary>Recompute the grid only (no run).</summary>
    public ICommand RefreshCommand { get; }

    /// <summary>Raise NAV-change signals → parent auto-triggers the scoped run.</summary>
    public ICommand CheckAndRunCommand { get; }

    /// <summary>Runtime constructor (DI).</summary>
    public NavSyncViewModel(ILogger logger, NavChangeDetector detector, NavSyncOptions options) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        CompanyLabel = string.IsNullOrWhiteSpace(_options.CompanyFilter)
            ? "All companies"
            : _options.CompanyFilter;
        YrDbStatusText = string.IsNullOrWhiteSpace(_options.YieldRaccoonDbPath) ? "not set" : "configured";
    }

    /// <summary>Designer constructor.</summary>
    public NavSyncViewModel()
    {
        RefreshCommand = new AsyncCommand(OnRefreshAsync, () => !IsBusy);
        CheckAndRunCommand = new AsyncCommand(OnCheckAndRunAsync, () => !IsBusy && !IsRunning);
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        CompanyLabel = "Schroder";
        YrDbStatusText = "configured";
        SummaryText = "2 of 3 funds have new NAV data";
        Rows.Add(new NavSyncStatusRowViewModel("LU0000000123", "Schroder ISF Global Eq", "Schroder",
            "2026-06-05", "2026-06-01", "Changed", NavSyncStatusKind.Changed));
        Rows.Add(new NavSyncStatusRowViewModel("LU0000000456", "Schroder Asian Growth", "Schroder",
            "2026-06-05", "—", "New", NavSyncStatusKind.New));
        Rows.Add(new NavSyncStatusRowViewModel("LU0000000789", "Schroder Euro Corp Bond", "Schroder",
            "2026-06-05", "2026-06-05", "Up to date", NavSyncStatusKind.UpToDate));
    }

    private async Task OnRefreshAsync()
    {
        if (_detector is null) return;

        IsBusy = true;
        try
        {
            // No ConfigureAwait(false): resume on the UI thread so the
            // ObservableCollection mutations below are dispatcher-safe.
            var rows = await _detector.DescribeAsync();

            Rows.Clear();
            foreach (var row in rows)
                Rows.Add(NavSyncStatusRowViewModel.From(row));

            var willRaise = rows.Count(r => r.Kind is NavSyncStatusKind.New or NavSyncStatusKind.Changed);
            SummaryText = $"{willRaise} of {rows.Count} funds have new NAV data";
            HasLoaded = true;
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "NAV Sync refresh failed");
            SummaryText = "Refresh failed — see log.";
        }
        finally { IsBusy = false; }
    }

    private async Task OnCheckAndRunAsync()
    {
        if (_detector is null) return;

        IsBusy = true;
        try
        {
            var published = await _detector.DetectAndPublishAsync();
            _logger?.Info("NAV Sync: published {Count} signal(s)", published.Count);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "NAV Sync check-and-run failed");
        }
        finally { IsBusy = false; }
    }
}
