using System.IO;
using System.Reactive.Concurrency;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
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

        var output = await _agent.RunAsync(IsoWeek, RunId);

        var outPath = _paths.ThesisValidatorOutput(IsoWeek, RunId);
        if (File.Exists(outPath))
            OutputJson = await File.ReadAllTextAsync(outPath);

        OutputSummaryText = $"{output.Funds.Count} funds — theses validated";
    }
}
