using System.Diagnostics;

using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Raised once step 8 has written its output for a fund and completed its run. Carries no
/// payload — a reader takes <c>Step08Json</c> back by key. Step 9 consumes it as a trigger,
/// not as input: it asks its barrier whether every fund is now finished, and the pass reads
/// every fund's record, not this one's.
/// </summary>
/// <seealso cref="IStepDoneSignal"/>
[DebuggerDisplay("{Isin.Value,nq} @ {NavDate.Date,nq:yyyy-MM-dd} run {RunId.Value,nq}")]
public sealed record Step08DoneSignal(Isin Isin, DateTimeOffset NavDate, PipelineRunId RunId)
    : IStepDoneSignal;
