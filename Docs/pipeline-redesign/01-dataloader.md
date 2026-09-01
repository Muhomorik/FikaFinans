<!--
  Authoring rules: see README.md in this folder.
  STATUS: design only. Nothing here is implemented.
-->

# Step 1 — DataLoader

> **Related:**
>
> - The cross-cutting design this follows from:
>   [event-driven-orchestration-plan.md](./event-driven-orchestration-plan.md)
> - What this step does **today** — I/O schemas, failure modes, test
>   fixtures:
>   [01-dataloader.md](../../FikaFinans.InfrastructureV2.Tests/docs/01-dataloader.md)

`StepId.DataLoader`, implemented by `DataLoaderAgent` behind
`IDataLoaderAgent`, surfaced in the desktop app as
`Step1DataLoaderViewModel`. This page covers only what the move to
signal-driven, per-fund processing changes.

## Sketch — the same step, two hosts

Illustrative only. Names marked *(new)* do not exist yet; queue names are
owned by [backend-nav-sync-plan.md](../backend-nav-sync-plan.md).

### What is the same in both hosts

The step depends on interfaces and gets them injected. Only the
registration differs, so this table is the whole difference:

| Interface | Local | Cloud |
| --- | --- | --- |
| `INavSignalPublisher` | `LocalRxNavSignalBus` | queue publisher *(new)* |
| `INavSignalSource` | `LocalRxNavSignalBus` | — nothing subscribes |
| `IIsinProgressRepository` | SQLite | Tables |
| `NavSyncOptions` | built from `AppSettings` | bound from app settings |

Two databases are involved, and this holds for every step:

| Database | Holds | Local | Cloud |
| --- | --- | --- | --- |
| FikaFinans | progress rows, positions, the NAV mirror | SQLite | Azure Tables |
| YieldRaccoon | fund identity and NAV history — read-only, someone else's | its SQLite file, read directly | REST call to the YieldRaccoon backend |

The YieldRaccoon row is the **fetch seam**: one interface, a
SQLite-backed implementation locally and a REST-backed one in cloud.
Steps reference neither database directly.

### Composition root

Autofac locally — `InfrastructureModule`, already doing this. The
isolated worker model is plain ASP.NET Core DI, same `IServiceCollection`
and same lifetimes, and `Program.cs` is ours to write:

```csharp
var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.Configure<NavSyncOptions>(
    builder.Configuration.GetSection("NavSync"));          // CompanyFilter, etc.
builder.Services.AddSingleton<INavSignalPublisher, QueueNavSignalPublisher>();  // (new)
builder.Services.AddSingleton<IIsinProgressRepository, TablesIsinProgressRepository>();  // (new)
builder.Services.AddSingleton<IPipelineFrontDoor, PipelineFrontDoor>();         // (new)

builder.Build().Run();
```

Settings therefore reach the step the way they already do in WPF —
injected, not read from a static. `NavSyncOptions` already carries
`CompanyFilter`: same record, same injection, different source.
(`CompanyFilter` itself is read by the detector, not by this step — see
Input trigger.)

One cloud-only caveat: values used *inside* trigger attributes
(`%NavChangedQueue%`) must come from real application settings. The
platform resolves those before the worker starts, so a custom
configuration source cannot supply them.

### Handler shape

**Rule — applies to every step, not just this one.** A step handler has
one signature, identical in both hosts:

```text
HandleAsync(<TSignal> signal, CancellationToken ct)
```

| | Goes where | Why |
| --- | --- | --- |
| Stable dependencies — repositories, publisher, options, logger | constructor | resolved once, substitutable in tests |
| The signal | parameter | it is the only per-invocation input |
| `CancellationToken` | parameter | host-owned shutdown, not a dependency |

Two things follow, and both are load-bearing:

- **One signal, never a batch.** A handler that took a list would only
  be callable from the host that produces lists. Coalescing, batching
  and fan-out are transport concerns and stay above the call.
- **No transport type in the handler's dependency set** — no `IObservable`,
  no `QueueClient`, no Functions binding attributes — so it is
  unit-testable with substituted dependencies and needs no host fixture.

### Local

#### Send signal

Through `INavSignalPublisher`, resolved to `LocalRxNavSignalBus`. The
step does not know it is in WPF:

```csharp
await _publisher.PublishAsync(
    [new Step01DoneSignal(isin, navDate, runId)], ct);   // (new)
```

#### Receive signal

The composition root subscribes. Coalescing happens here, above the
call — buffer, drop duplicate funds, flatten back to one signal per
invocation:

```csharp
_navSource.Signals                                  // INavSignalSource
    .Buffer(TimeSpan.FromMilliseconds(250))         // coalesce…
    .SelectMany(batch => batch.DistinctBy(s => s.Isin))   // …then flatten
    .Subscribe(signal => _frontDoor.HandleAsync(signal, ct));   // (new)
```

### Cloud

#### Send signal

Same publisher call as local — only the registered implementation
changes, to one backed by a queue client:

```csharp
await _publisher.PublishAsync(
    [new Step01DoneSignal(isin, navDate, runId)], ct);   // (new)
```

The host can also do the sending, by returning the message instead of
publishing it. That makes "write the column, then emit" structural — the
host enqueues only after the invocation returns, so a throwing step
cannot advance the chain. **Not decided** — and it is the only thing that
touches the handler-shape rule, since it changes the return type
(`Task` versus `Task<Step01DoneSignal?>`). Whichever wins, both hosts
take it.

```csharp
[Function("Step01DataLoader")]
[QueueOutput("%Step02Queue%")]                        // isolated worker:
public async Task<Step01DoneSignal[]> Run(...)        // return value = the send
{
    var next = await _frontDoor.HandleAsync(signal, ct);
    return next is null ? [] : [next];                // rejected → send nothing
}
```

#### Receive signal

Nothing subscribes. A **public class with a public method** carrying the
attribute is the whole listener — the Functions host polls, dequeues and
invokes it. An *instance* method, so the rule's constructor injection
works here too:

```csharp
public sealed class Step01DataLoaderFunction                  // (new)
{
    private readonly IPipelineFrontDoor _frontDoor;           // (new)
    private readonly ILogger<Step01DataLoaderFunction> _logger;

    public Step01DataLoaderFunction(
        IPipelineFrontDoor frontDoor,
        ILogger<Step01DataLoaderFunction> logger)
    { _frontDoor = frontDoor; _logger = logger; }

    [Function("Step01DataLoader")]
    public Task Run(
        [QueueTrigger("%NavChangedQueue%")] NavChangeSignal signal,
        CancellationToken ct)
        => _frontDoor.HandleAsync(signal, ct);
}
```

Both hosts end in the identical call; what differs is only how it is
reached — a subscription we own versus an attribute the host reads — and
that difference stops at the call site.

## Input trigger

`NavChangeSignal` — one per fund, per trading date. `Isin` and `NavDate`,
unchanged. Same record in both environments.

| | Producer | Transport | How the step is reached |
| --- | --- | --- | --- |
| Local | `INavChangeDetector` → `INavSignalPublisher` | `LocalRxNavSignalBus` | subscribe to `INavSignalSource.Signals` |
| Cloud | producer backend | front-door queue | **nothing subscribes** — the Functions host polls the queue and invokes the step once per message |

Queue mechanics — poll interval, visibility, poison thresholds — are
owned by [backend-nav-sync-plan.md](../backend-nav-sync-plan.md).
Delivery is **at-least-once**: the same signal can arrive twice, and the
progress-row check plus latest-only column overwrite are what make that
safe.

### Manual trigger — WPF only

The debug button on `Step1DataLoaderViewModel` stays. It is a desktop
affordance with no cloud counterpart: the Functions host has no UI and
nothing to press.

It bypasses detection, not transport — the command goes through
`IPipelineRunner` and raises the same events as a signal-driven run, so
the tab's progress rows and logs look identical either way.

## Input data — what the step reads

Reads only. The step works today on producer-precomputed metrics; after
the change it reads the raw series instead and computes them itself, so
two file inputs are replaced by one fetch — see the next section.

| Input data | Current | WPF | Cloud |
| --- | --- | --- | --- |
| **Identity slice** — static per-fund facts: name, fee, category, risk, owner count. The spine; no row here means the fund does not exist downstream | `YieldRaccoon_metadata_{family}_{iso_week}.csv` | YieldRaccoon SQLite database | REST call to the YieldRaccoon backend |
| **Bucketed history metrics** — producer-computed, 26 non-overlapping two-week buckets per fund. Five of them (`best_day_pct`, `worst_day_pct`, `pct_positive_days`, `skewness`, `ann_volatility_2w_pct`) are daily-return statistics the four sample points cannot recover | `YieldRaccoon_summary_{family}_{iso_week}.csv` → `NavBucket` | **stops being an input** — computed from the mirrored series | same |
| **Rolling-window metrics** — producer-computed 12-week and 1-year figures | `YieldRaccoon_snapshot_{family}_{iso_week}.csv` → `FundSnapshot` | **stops being an input** — computed from the mirrored series | same |
| **Positions** — what is currently held | `positions.csv` | `SqlitePositionsRepository` — already migrated | `IPositionsRepository`, cloud store still open |
| **Pinnings** — hand-written layer overrides: `core` or `writeoff`, everything else is an active position | `portfolio_structure.md` | unchanged — still parsed from the file by `PortfolioStructureMdParser` | still open |
| Step configs (2, 4, 9, 10) | `config-NN-*.json` in the inputs folder | unchanged | still open — not producer data |
| **Inputs folder** — the filesystem root every file-backed row above resolves against | settings path via `IPathsService` | unchanged — `SettingsBackedPathsService` | **no filesystem** — every remaining file row must find another home first |

The first two rows go through the fetch seam. Only they differ by host;
the rest is unchanged or still open.

Two reading rules survive the move: the identity fetch honours the option
filters, so an excluded fund produces no record at all, and the series
fetch is a **delta** — rows newer than the mirror already holds, not the
full history.

### What a frontend reads when the user opens the view

Neither frontend runs the step. Opening a tab or a page is a **read of
the persisted output** — `Step01Json` on `IsinProgressEntity`, keyed by
run id — never a trigger.

| Frontend | Path to the data | Implementation behind the interface |
| --- | --- | --- |
| WPF tab | `Step1DataLoaderViewModel.LoadOutputAsync` → `IStepOutputReader` *(new)* | SQLite-backed, resolved by Autofac |
| Next.js page | fetch over the backend API → the same `IStepOutputReader` *(new)* | Tables-backed, resolved in the backend's composition root |

One interface, one signature, two registrations — the same seam as the
step handler. The frontends differ only in how they reach it: WPF calls
it in-process from a ViewModel, Next.js reaches it through an API
endpoint that does not exist yet.

Today the read is [`IsinProgressOutputLoader.LoadStepFundsAsync`](../../FikaFinans.Wpf/Services/IsinProgressOutputLoader.cs)
— `internal static`, living in `FikaFinans.Wpf`, so no backend can call
it. It has to move down behind the interface. Two changes it needs on the
way: the repository stops being a parameter and becomes a constructor
dependency, and the `Func<IsinProgressEntity, string?>` column selector
gives way to a `StepId`, so the signature carries no storage type.

Two cases, and both are normal:

| State | What the view shows |
| --- | --- |
| Column populated | the step's last output for that run id |
| Row claimed, column still empty | skeleton — the fund is in flight, output is coming |
| No row | placeholder — "not processed yet", with the fund's identity if we have it |

Neither empty state is an error, and neither starts a run. The view holds
its placeholder until a signal drives the step and the column is written.

All three states come from the same call. Neither frontend branches on
the host it runs in.

## Pre-processing — assemble the agent input

What the step builds from those reads. The two metric slices arrive as
producer files today; after the change they are ours to compute, from the
mirrored series, in both hosts.

The metric computation already exists in the YieldRaccoon repo — we do not
write it, we copy it.

| Agent input | Built from | Code that already does it |
| --- | --- | --- |
| Bucketed history metrics | mirrored NAV series | `FundStatisticsCsvExportService.SliceIntoWindows` cuts the series into non-overlapping windows; `FundStatisticsCalculator.Compute` returns one `FundSummaryStatistics` per window |
| Rolling-window metrics | mirrored NAV series | `FundSnapshotStatisticsCalculator.Compute` returns one `FundSnapshotStatistics` per fund from a 12-week and a 1-year slice |
| Fund record | identity + both slices + positions + pinnings | ours — join on ISIN; write-off pins diverted to the frozen list, the rest tagged core or active |
| Data-quality warnings | all of the above | ours — accumulated while joining |

Both calculators are pure math: NAV values in, a typed record out, no I/O
and no database.

The layer above them owns its own read and write, the same problem as
`Run` in the next section. `IFundStatisticsCsvExportService.ExportAsync`
and its snapshot twin open a SQLite path themselves, write a CSV to a path,
and return a row count — so calling one means handing it a database this
step does not have and taking back a file it cannot read.

Nothing needs adding on the YieldRaccoon side, though: the CSV is written
*after* the calculators run, and the calculators are the part we want.

**TODO — copy into this repo.** All of these live in
`YieldRaccoon.Infrastructure/Services/` and are `internal`, so a project
reference would not reach them even if we wanted one:

| Copy | For |
| --- | --- |
| `FundStatisticsCalculator.Compute` | per-bucket metrics |
| `FundSnapshotStatisticsCalculator.Compute` | 12-week and 1-year metrics |
| `FundStatisticsCsvExportService.SliceIntoWindows` | the bucketing, including the rule that drops windows under 7 days |
| `FundSummaryStatistics`, `FundSnapshotStatistics` | the return types |

### Does the output fit the agent?

Yes. Field names match one-for-one — `FundSummaryStatistics` minus
`Isin`/`Name` is [`NavBucket`](../../FikaFinans.Domain/Funds/NavBucket.cs),
`FundSnapshotStatistics` is [`FundSnapshot`](../../FikaFinans.Domain/Funds/FundSnapshot.cs).
The types do not, and that is the whole adapter:

| | YieldRaccoon | Ours |
| --- | --- | --- |
| Metric fields | `double` | `decimal` |
| Missing value | `double.NaN` | `null` (`decimal?`) |
| NAV bookends | `decimal` | `decimal` — no conversion |

`NaN → null` is not a workaround, it is the pipeline invariant: NaN means
insufficient data or a suppressed Sharpe, and it must never become zero.
Our nullability already matches where YieldRaccoon can produce NaN —
`Sharpe2w` alone on the bucket, all eight fields on the snapshot.

## Agent work

Code, not an LLM. Contract:
[01-dataloader.md](../../FikaFinans.InfrastructureV2.Tests/docs/01-dataloader.md)
— schemas, failure modes and vocabulary live there.

The call is
[`IDataLoaderAgent.Run(family, isoWeek, runId)`](../../FikaFinans.Application/Pipeline/Agents/IDataLoaderAgent.cs),
implemented by
[`DataLoaderAgent`](../../FikaFinans.Infrastructure/Pipeline/Agents/DataLoaderAgent.cs).

`Run` only wraps I/O: resolve paths, open the files, read positions, call
`RunInMemory`, write the JSON. `RunInMemory` is the join — parse, join,
return `DataLoaderOutput`, no disk. That is the call this step wants.

Two things stand in the way:

| | |
| --- | --- |
| `RunInMemory` is not on `IDataLoaderAgent` | the interface declares only `Run`, so the in-memory call is reachable only on the concrete class |
| Its inputs are `TextReader` | cloud would have to render REST results back into CSV to satisfy it |

**TODO:** put `RunInMemory` on `IDataLoaderAgent` so callers can get the
typed `DataLoaderOutput` back. It is already implemented — this only widens
the interface. `Run` stays as it is; step 2 needs the typed call.

## Post-processing — write and emit

Two writes with different lifetimes, then one signal.

| Written | The call | Where it lands | Local | Cloud |
| --- | --- | --- | --- | --- |
| This run's fund records | [`IStreamingPipelineGateway.ClaimIsinProgressAsync`](../../FikaFinans.Application/Pipeline/IStreamingPipelineGateway.cs) → [`IIsinProgressRepository.UpsertBatchAsync`](../../FikaFinans.Application/Storage/Bank/IIsinProgressRepository.cs) | `Step01Json` on `IsinProgressEntity`, partition `isin-progress`, row key ISIN — plus `CurrentStep` and `ProcessingStartedAt` | SQLite | Azure Tables |
| New raw NAV rows | [`IFundsRepository.UpsertNavAsync`](../../FikaFinans.Application/Storage/Bank/IFundsRepository.cs) | one `NavSnapshotEntity` per trading date, partition `nav/{isin}` | SQLite | Azure Tables |

Same two interfaces in both hosts; only the registration differs — the
seam from the first table on this page. Lifetimes do not match:
`Step01Json` is latest-only and cleared at the next claim, the NAV rows
accumulate and outlive every run.

Two things the write path needs:

| | |
| --- | --- |
| `ClaimIsinProgressAsync` fuses the claim and the `Step01Json` write | it has to split — the claim is the in-flight lock and must precede the fetch |
| `UpsertNavAsync` writes one row per call | a delta fetch produces many rows per fund; `IFundsRepository` batches `FundEntity` but has no NAV equivalent |

### Emit — nothing to reuse

`NavChangeSignal` is the only signal type in the repo, and it is this
step's *input*. Nothing is raised on completion, by any step.

Both seams name it in their signatures:

| Seam | Signature today |
| --- | --- |
| [`INavSignalPublisher`](../../FikaFinans.Application/Pipeline/Signals/INavSignalPublisher.cs) | `PublishAsync(IReadOnlyList<NavChangeSignal> signals, CancellationToken ct)` |
| [`INavSignalSource`](../../FikaFinans.Application/Pipeline/Signals/INavSignalSource.cs) | `IObservable<NavChangeSignal> Signals` |

So a step-2 trigger cannot travel over them as they stand.

**TODO:** add `Step01DoneSignal` *(new)*. It carries fund identifier,
trading date, run id — **no payload**, step 2 reads `Step01Json` back by
key. Next Rx hop locally, next queue in cloud.

Number only, no step name. The number is what orders the chain, and it is
already the key everywhere else — `StepId`, the `Step{N}Json` column, the
`%StepNNQueue%` setting. Repeating the step's name here would duplicate
what `StepId` already maps and would force a rename of the wire type the
day a step is renamed. Steps 2–9 follow the pattern — `Step02DoneSignal`,
`Step04DoneSignal`, and so on.

#### Two families, and the difference is real

`NavChangeSignal` **enters** the pipeline; a done-signal **hands off
inside** it. That is not only a difference of origin — the shapes differ.
`NavChangeSignal` has no `RunId` and cannot have one: no run exists until
the front door creates it. Every done-signal has one.

Sketch — the hierarchy that says so:

```csharp
public interface IPipelineSignal;                     // (new) marker

public interface IStepDoneSignal : IPipelineSignal    // (new) step N → step N+1
{
    Isin Isin { get; }
    DateTimeOffset NavDate { get; }
    PipelineRunId RunId { get; }                      // a run exists by now
}

public sealed record NavChangeSignal(Isin Isin, DateTimeOffset NavDate)
    : IPipelineSignal;                                // inbound — no RunId
```

`NavChangeSignal` is then visibly a signal and visibly not a hand-off.

#### One publisher per family

| Publisher | Publishes | Produced by | Transport |
| --- | --- | --- | --- |
| [`INavSignalPublisher`](../../FikaFinans.Application/Pipeline/Signals/INavSignalPublisher.cs) — exists | `NavChangeSignal` | `INavChangeDetector` locally, the YieldRaccoon backend in cloud | front-door queue |
| `IPipelineSignals` *(new)* | every `IStepDoneSignal` | ours, always | step queues |

**TODO:** add `IPipelineSignals` *(new)* — the whole outbound surface of
the pipeline, one overload per signal:

```csharp
Task PublishAsync(Step01DoneSignal signal, CancellationToken ct = default);
Task PublishAsync(Step02DoneSignal signal, CancellationToken ct = default);
// … one per step; the interface *is* the list of everything we send
```

Overloads rather than a generic `ISignalPublisher<TSignal>`, for two
reasons:

| | |
| --- | --- |
| No signal-type → queue-name map | the cloud implementation names `%Step02Queue%` in the body of the step-1 overload — ordinary code, read top to bottom, instead of a dictionary or an attribute |
| Adding a step breaks the build | until its overload is implemented — a generic seam would surface the same gap as a missing registration at run time |

The cost is nine methods on one interface and a test double implementing
all nine. The step count is fixed, so it does not grow.

The receive side is local-only — the asymmetry `INavSignalSource` already
has. `IPipelineSignalStreams` *(new)*, one `IObservable<T>` property per
signal. Cloud subscribes to nothing; the Functions host reads attributes.

Ten steps means nine done-signals. Decided once here, inherited by every
later step file.

**Write the column, then emit.** Not atomic. A crash between the two
replays this step, which is safe — the column is latest-only overwrite.
The reverse order can advance the chain past a step whose output was
never written.
