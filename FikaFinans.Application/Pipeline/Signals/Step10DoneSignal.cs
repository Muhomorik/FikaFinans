using System.Diagnostics;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once a step 10 run has stored the day's trades. One per run, naming no fund, like
/// <see cref="Step09DoneSignal"/>. Carries no payload, as every done-signal: the bank side
/// reads the trades back through the store, so nothing travelling here can disagree with
/// what was stored — and the message stays far under a queue's size cap.
/// </summary>
/// <param name="RunId">The run's own id — the one the stored trades carry.</param>
[DebuggerDisplay("step 10 run {RunId.Value,nq}")]
public sealed record Step10DoneSignal(PipelineRunId RunId) : IPipelineSignal;
