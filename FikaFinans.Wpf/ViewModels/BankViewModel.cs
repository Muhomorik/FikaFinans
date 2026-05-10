using System.Collections.ObjectModel;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Application.Paths;
using FikaFinans.Domain.Bank.Trading;
using FikaFinans.Infrastructure.Bank;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class BankViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger? _logger;
    private readonly IScheduler? _uiScheduler;
    private readonly IPortfolioQueryService? _portfolioQuery;
    private readonly ITradingService? _tradingService;
    private readonly ILedgerService? _ledgerService;
    private readonly ISettlementEngine? _settlementEngine;
    private readonly BankSimulator? _clock;
    private readonly IBankCsvImporter? _csvImporter;
    private readonly IPathsService? _paths;
    private readonly CompositeDisposable _disposables = new();

    private string _cashText = "0 SEK";
    private string _totalValueText = "0 SEK";
    private string _pnlText = "0 SEK";
    private string _simTimeText = "—";
    private bool _isMarketOpen;
    private bool _isBusy;

    public string CashText
    {
        get => _cashText;
        set => SetProperty(ref _cashText, value, nameof(CashText));
    }

    public string TotalValueText
    {
        get => _totalValueText;
        set => SetProperty(ref _totalValueText, value, nameof(TotalValueText));
    }

    public string PnlText
    {
        get => _pnlText;
        set => SetProperty(ref _pnlText, value, nameof(PnlText));
    }

    public string SimTimeText
    {
        get => _simTimeText;
        set => SetProperty(ref _simTimeText, value, nameof(SimTimeText));
    }

    public bool IsMarketOpen
    {
        get => _isMarketOpen;
        set => SetProperty(ref _isMarketOpen, value, nameof(IsMarketOpen));
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value, nameof(IsBusy));
    }

    public ObservableCollection<FundPositionDto> Holdings { get; } = new();
    public ObservableCollection<TradingOrder> PendingOrders { get; } = new();
    public ObservableCollection<LedgerRowViewModel> LedgerRows { get; } = new();

    public ICommand LoadedCommand { get; }
    public ICommand TickDayCommand { get; }
    public ICommand SettleNowCommand { get; }
    public ICommand ReloadCsvCommand { get; }
    public ICommand ExportCsvCommand { get; }

    /// <summary>Runtime constructor (DI).</summary>
    public BankViewModel(
        ILogger logger,
        IScheduler uiScheduler,
        IPortfolioQueryService portfolioQuery,
        ITradingService tradingService,
        ILedgerService ledgerService,
        ISettlementEngine settlementEngine,
        BankSimulator clock,
        IBankCsvImporter csvImporter,
        IPathsService paths) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
        _portfolioQuery = portfolioQuery ?? throw new ArgumentNullException(nameof(portfolioQuery));
        _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        _ledgerService = ledgerService ?? throw new ArgumentNullException(nameof(ledgerService));
        _settlementEngine = settlementEngine ?? throw new ArgumentNullException(nameof(settlementEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _csvImporter = csvImporter ?? throw new ArgumentNullException(nameof(csvImporter));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    /// <summary>Designer constructor.</summary>
    public BankViewModel()
    {
        LoadedCommand = new AsyncCommand(OnLoadedAsync);
        TickDayCommand = new AsyncCommand(OnTickDayAsync, () => !IsBusy);
        SettleNowCommand = new AsyncCommand(OnSettleNowAsync, () => !IsBusy);
        ReloadCsvCommand = new AsyncCommand(OnReloadCsvAsync, () => !IsBusy);
        ExportCsvCommand = new AsyncCommand(OnExportCsvAsync, () => !IsBusy);
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        CashText = "245 320 SEK";
        TotalValueText = "1 042 880 SEK";
        PnlText = "+18 240 SEK";
        SimTimeText = "Wed 14:32 · market open";
    }

    protected override void OnInitializeInRuntime()
    {
        base.OnInitializeInRuntime();

        if (_clock is not null)
        {
            UpdateSimTime(_clock.Now);

            _disposables.Add(_clock.TimeChanged
                .ObserveOn(_uiScheduler ?? Scheduler.CurrentThread)
                .Subscribe(t => UpdateSimTime(t)));
        }

        if (_settlementEngine is not null)
        {
            _disposables.Add(_settlementEngine.OrderSettled
                .ObserveOn(_uiScheduler ?? Scheduler.CurrentThread)
                .Subscribe(evt => { _ = RefreshAllAsync(); }));

            _settlementEngine.Start();
        }
    }

    private async Task OnLoadedAsync()
    {
        if (_csvImporter is not null && _paths is not null)
            await _csvImporter.ImportAsync(_paths.PositionsCsv);
        await RefreshAllAsync();
    }

    private async Task OnTickDayAsync()
    {
        if (_clock is null) return;
        IsBusy = true;
        try
        {
            _clock.AdvanceToNextDay();
            await RefreshAllAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task OnSettleNowAsync()
    {
        if (_clock is null) return;
        IsBusy = true;
        try
        {
            // Advance to night triggers NightTick which the SettlementEngine is subscribed to.
            _clock.AdvanceToNight();
            await Task.Delay(200); // let the reactive pipeline flush
            await RefreshAllAsync();
        }
        finally { IsBusy = false; }
    }

    private async Task OnReloadCsvAsync()
    {
        if (_csvImporter is null || _paths is null) return;
        IsBusy = true;
        try
        {
            await _csvImporter.ReimportAsync(_paths.PositionsCsv);
            await RefreshAllAsync();
        }
        finally { IsBusy = false; }
    }

    private Task OnExportCsvAsync()
    {
        _logger?.Info("Export positions.csv — not yet implemented");
        return Task.CompletedTask;
    }

    private async Task RefreshAllAsync()
    {
        if (_portfolioQuery is null || _tradingService is null || _ledgerService is null)
            return;

        try
        {
            var cash = await _portfolioQuery.GetAvailableCashAsync();
            var total = await _portfolioQuery.GetTotalPortfolioValueAsync();
            var positions = await _portfolioQuery.GetFundPositionsAsync();
            var orders = await _tradingService.GetPendingOrdersAsync();
            var transactions = await _ledgerService.GetAllTransactionsAsync();

            var scheduler = _uiScheduler ?? Scheduler.CurrentThread;
            scheduler.Schedule(() =>
            {
                CashText = $"{cash.Amount:N0} {cash.Currency}";
                TotalValueText = $"{total.Amount:N0} {total.Currency}";

                var costBasisTotal = positions.Sum(p => p.CostBasis.Amount);
                var pnl = total.Amount - costBasisTotal - cash.Amount;
                PnlText = $"{(pnl >= 0 ? "+" : "")}{pnl:N0} {cash.Currency}";

                Holdings.Clear();
                foreach (var p in positions) Holdings.Add(p);

                PendingOrders.Clear();
                foreach (var o in orders) PendingOrders.Add(o);

                LedgerRows.Clear();
                foreach (var txn in transactions.OrderByDescending(t => t.Timestamp))
                {
                    foreach (var entry in txn.Entries)
                    {
                        LedgerRows.Add(new LedgerRowViewModel(
                            txn.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                            entry.AccountId.ToString(),
                            entry.GetDebit().Amount > 0 ? entry.GetDebit().Amount.ToString("N2") : "",
                            entry.GetCredit().Amount > 0 ? entry.GetCredit().Amount.ToString("N2") : "",
                            txn.Description));
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to refresh bank data");
        }
    }

    private void UpdateSimTime(DateTimeOffset t)
    {
        var marketState = (_clock?.IsMarketOpen == true) ? "market open" : "market closed";
        SimTimeText = $"{t:ddd dd-MMM-yyyy HH:mm} · {marketState}";
        IsMarketOpen = _clock?.IsMarketOpen == true;
    }

    public void Dispose() => _disposables.Dispose();
}

public sealed record LedgerRowViewModel(string Date, string Account, string Debit, string Credit, string Memo);
