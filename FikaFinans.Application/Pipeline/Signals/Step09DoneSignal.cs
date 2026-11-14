using System.Diagnostics;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once a step 9 pass has stored its records. One per pass, not per fund — a pass is
/// over the whole universe — so unlike steps 1–8's it names no fund and no trading date, and
/// is not an <see cref="IStepDoneSignal"/>. Carries no payload: step 10 consumes it as a
/// trigger and reads every fund's <c>Step09Json</c> back off the rows.
/// </summary>
/// <param name="RunId">The pass's own run id — for log lines; no row carries it.</param>
[DebuggerDisplay("step 9 pass {RunId.Value,nq}")]
public sealed record Step09DoneSignal(PipelineRunId RunId) : IPipelineSignal;
