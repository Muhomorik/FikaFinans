using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step10PortfolioConstructorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IPortfolioConstructorAgent? _agent;
    private readonly ISendToBankService? _sendToBank;

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
        ISendToBankService sendToBank,
        IConfigEditorDialogService configEditor)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _sendToBank = sendToBank;
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

        await Task.Run(() => _agent.Run(IsoWeek, RunId));
        await LoadOutputAsync();
    }

    public override async Task LoadOutputAsync()
    {
        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.PortfolioConstructorOutput(IsoWeek, RunId);
        if (!File.Exists(outPath))
        {
            OutputSummaryText = "Output file not found";
            return;
        }

        var json = await File.ReadAllTextAsync(outPath);
        OutputJson = json;

        var output = JsonSerializer.Deserialize<TradesOutput>(json, JsonOptions.Default);
        if (output is null)
        {
            OutputSummaryText = "Output file present but unreadable";
            return;
        }

        _lastOutput = output;
        _sendToBankCommand.RaiseCanExecuteChanged();
        OutputSummaryText = $"{output.Trades.Count} trades · {output.ConstraintViolations.Count} violations";
    }

    private bool CanSendToBank() => _lastOutput is not null && _sendToBank is not null;

    private async Task SendToBankAsync()
    {
        if (_lastOutput is null || _sendToBank is null)
            return;

        var result = await _sendToBank.SubmitAsync(_lastOutput);

        OutputSummaryText =
            $"{_lastOutput.Trades.Count} trades · {_lastOutput.ConstraintViolations.Count} violations · " +
            $"{result.Sent} sent, {result.Skipped} skipped";
    }
}
