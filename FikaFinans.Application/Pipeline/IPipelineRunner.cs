namespace FikaFinans.Application.Pipeline;

public interface IPipelineRunner
{
    IObservable<StepEvent> Events { get; }

    Task<bool> RunAllAsync(string family, string isoWeek, string runId, CancellationToken ct = default);

    Task<bool> RunStepAsync(int stepNumber, string family, string isoWeek, string runId, CancellationToken ct = default);
}
