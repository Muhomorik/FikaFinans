using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step8RecommenderViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IRecommenderAgent? _agent;

    public override int StepNumber => 8;
    public override string AgentName => "Recommender";
    public override bool HasConfig => false;

    public Step8RecommenderViewModel() { }

    public Step8RecommenderViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IRecommenderAgent agent)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
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
