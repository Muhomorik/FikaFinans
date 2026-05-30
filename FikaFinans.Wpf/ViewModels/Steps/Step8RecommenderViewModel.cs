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

public sealed class Step8RecommenderViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IRecommenderAgent? _agent;
    private readonly IIsinProgressRepository? _isinProgress;

    public override int StepNumber => 8;
    public override string AgentName => "Recommender";
    public override bool HasConfig => false;

    public Step8RecommenderViewModel() { }

    public Step8RecommenderViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IRecommenderAgent agent,
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
        // 8c: prefer SQLite Step08Json columns; disk fallback for per-step button flow.
        if (_isinProgress is not null)
        {
            var sqliteResult = await IsinProgressOutputLoader.LoadStepFundsAsync(
                _isinProgress, RunId, row => row.Step08Json);
            if (sqliteResult is not null)
            {
                OutputJson = sqliteResult.Json;
                OutputSummaryText = $"{sqliteResult.Funds.Count} funds — recommendations generated";
                return;
            }
        }

        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.RecommenderOutput(IsoWeek, RunId);
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
            : $"{output.Funds.Count} funds — recommendations generated";
    }
}
