using System.IO;
using System.Reactive.Concurrency;
using System.Text.Json;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Infrastructure.Pipeline.Json;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

public sealed class Step7ThesisValidatorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IThesisValidatorAgent? _agent;

    public override int StepNumber => 7;
    public override string AgentName => "Thesis validator";
    public override bool HasConfig => false;

    public Step7ThesisValidatorViewModel() { }

    public Step7ThesisValidatorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths, IThesisValidatorAgent agent)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _agent = agent;
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
        if (_paths is null || string.IsNullOrEmpty(IsoWeek)) return;

        var outPath = _paths.ThesisValidatorOutput(IsoWeek, RunId);
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
            : $"{output.Funds.Count} funds — theses validated";
    }
}
