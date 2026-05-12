using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Bank.Events;
using FikaFinans.Domain.Bank.Trading;
using FikaFinans.Infrastructure.Bank.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;

namespace FikaFinans.Infrastructure.Bank;

public class SettlementEngine : ISettlementEngine
{
    private readonly ILogger _logger;
    private readonly BankSimulator _clock;
    private readonly ITradingService _tradingService;
    private readonly IDbContextFactory<BankDbContext> _dbFactory;
    private readonly Subject<OrderSettledEvent> _orderSettled = new();
    private readonly Subject<SettlementRunCompleteEvent> _settlementRunComplete = new();
    private readonly CompositeDisposable _disposables = new();

    public IObservable<OrderSettledEvent> OrderSettled => _orderSettled.AsObservable();
    public IObservable<SettlementRunCompleteEvent> SettlementRunComplete => _settlementRunComplete.AsObservable();

    public SettlementEngine(
        ILogger logger,
        BankSimulator clock,
        ITradingService tradingService,
        IDbContextFactory<BankDbContext> dbFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _tradingService = tradingService ?? throw new ArgumentNullException(nameof(tradingService));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    }

    public void Start()
    {
        _disposables.Add(_clock.NightTick
            .Subscribe(async timestamp =>
            {
                try { await RunSettlementAsync(timestamp); }
                catch (Exception ex) { _logger.Error(ex); }
            }));

        _logger.Info("Settlement engine started, listening for night ticks");
    }

    private async Task RunSettlementAsync(DateTimeOffset settlementTime)
    {
        var pendingOrders = await _tradingService.GetPendingOrdersAsync();
        var settledCount = 0;

        _logger.Info("Settlement run starting: {0} pending orders at {1}", pendingOrders.Count, settlementTime);

        await using var db = await _dbFactory.CreateDbContextAsync();
        foreach (var order in pendingOrders)
        {
            var fund = await db.Funds
                .Include(f => f.NavHistory)
                .FirstOrDefaultAsync(f => f.Id == order.FundId);

            if (fund == null)
            {
                _logger.Warn("Fund {0} not found for order {1}, skipping", order.FundId, order.Id);
                continue;
            }

            var nav = fund.GetLatestNav();
            if (nav <= 0)
            {
                _logger.Warn("No valid NAV for fund {0}, skipping order {1}", fund.Name, order.Id);
                continue;
            }

            var result = await _tradingService.SettleOrderAsync(order.Id, nav);
            if (result.IsSuccess)
            {
                settledCount++;
                _orderSettled.OnNext(new OrderSettledEvent(
                    order.Id, order.FundId, order.Side, nav,
                    order.Side == OrderSide.Buy ? order.AmountValue / nav : order.Units!.Value,
                    settlementTime));
                _logger.Info("Settled order {0}: {1} {2} @ NAV {3:N2}", order.Id, order.Side, fund.Name, nav);
            }
            else
            {
                _logger.Warn("Failed to settle order {0}: {1}", order.Id, result.Errors[0].Message);
            }
        }

        _settlementRunComplete.OnNext(new SettlementRunCompleteEvent(settlementTime, settledCount));
        _logger.Info("Settlement run complete: {0} orders settled", settledCount);
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _orderSettled.Dispose();
        _settlementRunComplete.Dispose();
    }
}
