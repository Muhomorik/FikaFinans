using System.Diagnostics;

using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once step 5 has written its output for a fund. Carries no payload — a reader takes
/// <c>Step05Json</c> back by key. Step 6 consumes it.
/// </summary>
/// <seealso cref="IStepDoneSignal"/>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd} run {RunId.Value,nq}")]
public sealed record Step05DoneSignal(Isin Isin, DateTimeOffset NavDate, PipelineRunId RunId)
    : IStepDoneSignal;
