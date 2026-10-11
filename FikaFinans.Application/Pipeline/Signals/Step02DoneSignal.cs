using System.Diagnostics;

using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once step 2 has written its output. Carries no payload — a reader takes
/// <c>Step02Json</c> back by key. No step consumes it for work yet; step 2's tab listens
/// to learn which run to read.
/// </summary>
/// <seealso cref="IStepDoneSignal"/>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd} run {RunId.Value,nq}")]
public sealed record Step02DoneSignal(Isin Isin, DateTimeOffset NavDate, PipelineRunId RunId)
    : IStepDoneSignal;
