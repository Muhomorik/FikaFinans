using System.Diagnostics;

using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once step 3 has written its output for a fund. Carries no payload — a reader takes
/// <c>Step03Json</c> back by key, and the week's macro context from its own store. Step 4
/// consumes it.
/// </summary>
/// <seealso cref="IStepDoneSignal"/>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd} run {RunId.Value,nq}")]
public sealed record Step03DoneSignal(Isin Isin, DateTimeOffset NavDate, PipelineRunId RunId)
    : IStepDoneSignal;
