using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Storage.Bank;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step6CatalystTaggerViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly ICatalystTaggerAgent? _agent;
    private readonly IIsinProgressRepository? _isinProgress;

    public override int StepNumber => 6;
    public override string AgentName => "Catalyst tagger";
    public override bool HasConfig => false;

    public Step6CatalystTaggerViewModel() { }

    public Step6CatalystTaggerViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, ICatalystTaggerAgent agent,
        IIsinProgressRepository isinProgress)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
        _isinProgress = isinProgress;
    }

    protected override async Task RunStepCoreAsync()
    {
        if (_agent is null || _paths is null)
        {
            OutputSummaryText = "Configure Foundry credentials in Settings → Models";
            return;
        }
        if (string.IsNullOrEmpty(IsoWeek))
        {
            OutputSummaryText = "Select a week in the run bar first";
            return;
        }

        await _agent.RunAsync(IsoWeek, RunId);
        await LoadOutputAsync();
    }

    public override async Task LoadOutputAsync()
    {
        // 8c: prefer SQLite Step06Json columns; disk fallback for per-step button flow.
        if (_isinProgress is not null)
        {
            var sqliteResult = await IsinProgressOutputLoader.LoadStepFundsAsync(
                _isinProgress, RunId, row => row.Step06Json);
            if (sqliteResult is not null)
            {
                OutputJson = sqliteResult.Json;
                OutputSummaryText = $"{sqliteResult.Funds.Count} funds — catalysts tagged";
                return;
            }
        }

        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.CatalystTaggerOutput(IsoWeek, RunId);
        if (!File.Exists(outPath))
        {
            OutputSummaryText = "Output file not found";
            return;
        }

        var json = await File.ReadAllTextAsync(outPath);
        OutputJson = json;

        var output = JsonSerializer.Deserialize<DataLoaderOutput>(json, JsonOptions.Default);
        OutputSummaryText = output is null
            ? "Output file present but unreadable"
            : $"{output.Funds.Count} funds — catalysts tagged";
    }
}
