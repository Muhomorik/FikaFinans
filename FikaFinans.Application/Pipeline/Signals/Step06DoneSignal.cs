using System.Diagnostics;

using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once step 6 has written its output for a fund. Carries no payload — a reader takes
/// <c>Step06Json</c> back by key. No step consumes it for work yet.
/// </summary>
/// <seealso cref="IStepDoneSignal"/>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd} run {RunId.Value,nq}")]
public sealed record Step06DoneSignal(Isin Isin, DateTimeOffset NavDate, PipelineRunId RunId)
    : IStepDoneSignal;
