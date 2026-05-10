using System.IO;
using System.Reactive.Concurrency;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Portfolio;
using FluentResults;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step10PortfolioConstructorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IPortfolioConstructorAgent? _agent;
    private readonly ITradingService? _trading;
    private readonly IPortfolioQueryService? _portfolio;

    private TradesOutput? _lastOutput;
    private readonly AsyncCommand _sendToBankCommand;

    public override int StepNumber => 10;
    public override string AgentName => "Portfolio constructor";
    public override bool HasConfig => true;
    public override bool HasBank => true;
    public override ICommand SendToBankCommand => _sendToBankCommand;

    public Step10PortfolioConstructorViewModel()
    {
        _sendToBankCommand = new AsyncCommand(SendToBankAsync, CanSendToBank);
    }

    public Step10PortfolioConstructorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IPortfolioConstructorAgent agent,
        ITradingService trading, IPortfolioQueryService portfolio,
        IConfigEditorDialogService configEditor)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _trading = trading;
        _portfolio = portfolio;
        _configEditorService = configEditor;
        _sendToBankCommand = new AsyncCommand(SendToBankAsync, CanSendToBank);
    }

    protected override string? GetConfigPath() => _paths?.Config10PortfolioJson;

    protected override async Task RunStepCoreAsync()
    {
        if (_agent is null || _paths is null)
        {
            OutputSummaryText = "Configure data folder in Settings → Folders";
            return;
        }
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        var output = await Task.Run(() => _agent.Run(IsoWeek, RunId));
        _lastOutput = output;
        _sendToBankCommand.RaiseCanExecuteChanged();

        var outPath = _paths.PortfolioConstructorOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Trades.Count} trades · {output.ConstraintViolations.Count} violations";
    }

    private bool CanSendToBank() => _lastOutput is not null && _trading is not null && _portfolio is not null;

    private async Task SendToBankAsync()
    {
        if (_lastOutput is null || _trading is null || _portfolio is null)
            return;

        var positions = await _portfolio.GetFundPositionsAsync();
        var isinMap = positions.ToDictionary(p => p.Isin);

        int sent = 0, skipped = 0;
        foreach (var trade in _lastOutput.Trades)
        {
            if (trade.TradeType is TradeType.Hold or TradeType.NoOp)
                continue;

            if (!isinMap.TryGetValue(trade.Isin, out var pos))
            {
                Logger?.Warn("No bank fund found for ISIN {Isin} — skipping", trade.Isin);
                skipped++;
                continue;
            }

            Result<Domain.Bank.Identifiers.TradingOrderId> result;

            if (trade.TradeType is TradeType.Buy or TradeType.TopUp)
            {
                result = await _trading.CreateBuyOrderAsync(pos.FundId, Money.SEK(trade.AmountKr));
            }
            else if (trade.TradeType is TradeType.Sell)
            {
                result = await _trading.CreateSellOrderAsync(pos.FundId, pos.Units);
            }
            else // Trim, PartialSell
            {
                if (pos.Units <= 0) { skipped++; continue; }
                var navPerUnit = pos.CurrentValue.Amount / pos.Units;
                var units = navPerUnit > 0 ? trade.AmountKr / navPerUnit : 0m;
                if (units <= 0) { skipped++; continue; }
                result = await _trading.CreateSellOrderAsync(pos.FundId, units);
            }

            if (result.IsSuccess)
                sent++;
            else
            {
                Logger?.Warn("Order rejected for {Isin}: {Error}", trade.Isin, result.Errors[0].Message);
                skipped++;
            }
        }

        OutputSummaryText = $"{_lastOutput.Trades.Count} trades · {_lastOutput.ConstraintViolations.Count} violations · {sent} sent, {skipped} skipped";
    }
}
