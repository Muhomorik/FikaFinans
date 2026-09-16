using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Implemented by every hand-off from one step to the next: the fund, its trading date,
/// and the run they belong to. An inbound signal has no run, so it is never one of these.
/// </summary>
/// <seealso cref="Step01DoneSignal"/>
public interface IStepDoneSignal : IPipelineSignal
{
    Isin Isin { get; }

    DateTimeOffset NavDate { get; }

    PipelineRunId RunId { get; }
}
