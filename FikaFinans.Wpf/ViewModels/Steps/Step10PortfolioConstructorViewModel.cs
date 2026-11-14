using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Text.Json;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline;
using FikaFinans.Application.Pipeline.Steps;
using FikaFinans.Domain.Portfolio;
using FikaFinans.Infrastructure.Pipeline.Json;
using FikaFinans.Wpf.Services;
using JetBrains.Annotations;
using NLog;

namespace FikaFinans.Wpf.ViewModels.Steps;

/// <summary>
/// The step 10 tab: shows the last stored run, following the step's events rather than
/// driving them, with a debug button that runs step 10 again without storing it.
/// </summary>
public sealed class Step10PortfolioConstructorViewModel : StepViewModel
{
    private readonly IPathsService? _paths;
    private readonly IStep10PortfolioConstructor? _step10;
    private readonly ISendToBankService? _sendToBank;

    /// <summary>The last stored run — what Send to bank submits. Never a debug run.</summary>
    private TradesOutput? _lastStored;
    private readonly AsyncCommand _sendToBankCommand;

    public override int StepNumber => 10;
    public override string AgentName => "Portfolio constructor";

    // Kept, unlike step 9's: the config provider reads this file, so an edit applies to the
    // next run.
    public override bool HasConfig => true;
    public override bool HasBank => true;
    public override ICommand SendToBankCommand => _sendToBankCommand;

    [UsedImplicitly]
    public Step10PortfolioConstructorViewModel()   // designer only
    {
        _sendToBankCommand = new AsyncCommand(SendToBankAsync, CanSendToBank);
    }

    [UsedImplicitly]
    public Step10PortfolioConstructorViewModel(ILogger logger, IScheduler uiScheduler,
        IPathsService paths,
        IStep10PortfolioConstructor step10,
        ISendToBankService sendToBank,
        IConfigEditorDialogService configEditor,
        IStepEventSource stepEvents)
        : base(logger, uiScheduler)
    {
        _paths = paths;
        _step10 = step10;
        _sendToBank = sendToBank;
        _configEditorService = configEditor;
        _sendToBankCommand = new AsyncCommand(SendToBankAsync, CanSendToBank);

        // The tab follows the step rather than running it: whoever drove the run — step 9's
        // done-signal or the daily timer locally, a Function in Azure — reports through this
        // stream.
        var ticks = stepEvents.Events
            .Where(tick => tick.Step == StepId.PortfolioConstructor)
            .ObserveOn(uiScheduler);

        Disposables.Add(ticks.Subscribe(ApplyStepEvent));

        // The run's own Succeeded is the cue to reload — the done-signal goes to the bank side.
        Disposables.Add(ticks
            .Where(tick => tick.Kind == StepEventKind.Succeeded)
            .Select(_ => Observable
                .FromAsync(LoadOutputAsync)
                // Caught per load: an error reaching Subscribe would end the stream for the
                // session, and the next run's output would never show.
                .Catch((Exception ex) =>
                {
                    Logger?.Error(ex, "Step 10 output load failed");
                    return Observable.Empty<System.Reactive.Unit>();
                }))
            .Concat()
            .Subscribe());
    }

    protected override string? GetConfigPath() => _paths?.Config10PortfolioJson;

    /// <summary>
    /// The debug button: a run over the step 9 records and holdings as they stand, through the
    /// step, shown but never stored — a stored run is what the bank side acts on. Not sendable
    /// for the same reason. Free: no LLM.
    /// </summary>
    protected override async Task RunStepCoreAsync()
    {
        // Null only under the designer's parameterless constructor.
        if (_step10 is null) return;

        var output = await _step10.ComputeOutputAsync();

        OutputJson = JsonSerializer.Serialize(output, JsonOptions.Default);
        OutputSummaryText =
            $"Debug run, not stored, not sendable — {Summary(output)} — week {output.IsoWeek}";
    }

    /// <summary>
    /// Shows the last stored run, so the tab is filled whether it is opened before or after a
    /// run. Later runs reload through the Succeeded tick. No orphaned runs to fail, unlike
    /// steps 1–8: step 10 holds no row.
    /// </summary>
    protected override async Task OnFirstOpenedAsync()
    {
        await LoadOutputAsync();
    }

    /// <summary>
    /// Reads the last stored run back off the rows — the newest run's <c>Step10Json</c>
    /// slices, not the run bar's run: step 10 runs outside any fund's run.
    /// </summary>
    public override async Task LoadOutputAsync()
    {
        if (_step10 is null) return;

        var output = await _step10.ReadOutputAsync();

        _lastStored = output;
        _sendToBankCommand.RaiseCanExecuteChanged();

        if (output is null)
        {
            OutputSummaryText = "No step 10 run stored yet";
            return;
        }

        OutputJson = JsonSerializer.Serialize(output, JsonOptions.Default);
        OutputSummaryText = $"{Summary(output)} — week {output.IsoWeek}, {output.GeneratedAt}";
    }

    private static string Summary(TradesOutput output)
        => $"{output.Trades.Count} trades · {output.ConstraintViolations.Count} violations";

    private bool CanSendToBank() => _lastStored is not null && _sendToBank is not null;

    /// <summary>
    /// Submits the last stored run by hand. Whether the button stays once the bank side
    /// consumes step 10's done-signal is open on step 10's page.
    /// </summary>
    private async Task SendToBankAsync()
    {
        if (_lastStored is null || _sendToBank is null)
            return;

        var result = await _sendToBank.SubmitAsync(_lastStored);

        OutputSummaryText = $"{Summary(_lastStored)} · {result.Sent} sent, {result.Skipped} skipped";
    }
}
