<!--
  STATUS: PHASES 1+2+3 SHIPPED 2026-05-10..2026-05-12; Phases 4-7 still
  open. Per-phase status is annotated inline in §8 (Migration phases).

  Authoring rules for AI assistants and humans editing this file:
  - DO NOT write code (no C#, no XAML, no JSON config snippets, no shell).
  - DO use Mermaid diagrams to express architecture, flows, and state.
  - Prose stays at the "what / why / where it lives" level — no API
    signatures, no class names, no method bodies. Implementation lives
    alongside the code; this doc captures the intent.
  - DO NOT modify other documents from this plan. Cross-references are
    one-way: link out from this file to other docs, but never edit those
    other docs to point back here.
  - DO NOT invent architecture. If a piece of the flow is not yet decided,
    write it as an open question, not as a confident design.
  - When marking a phase done, link to the relevant code in the repo so
    the doc stays a navigable map of what landed.
-->

# Storage Migration & Positions Table — Feature Plan

> **Related:**
> - [Docs/backend-nav-sync-plan.md](./backend-nav-sync-plan.md) — the
>   queue-driven Function pipeline this storage feeds.
> - [FikaFinans.InfrastructureV2.Tests/docs/pipeline-plan.md](../FikaFinans.InfrastructureV2.Tests/docs/pipeline-plan.md) —
>   the 10-step pipeline that reads positions in Step 1 and writes orders
>   in Step 10.

## Context

Three things in the current codebase don't match where the rest of the
plan is heading:

1. **There is no SQLite database today.** `BankDbContext` uses
   `UseInMemoryDatabase("FikaFinansBankDb")` —
   [BankDbContext.cs](../FikaFinans.Infrastructure/Bank/Persistence/BankDbContext.cs).
   No `.db` file on disk, no EF migrations folder. State evaporates on
   process exit. "Migrating to SQLite" is a first-time install, not a
   schema change.
2. **Positions are still a CSV input.**
   [PositionsCsvParser.cs](../FikaFinans.Infrastructure/Pipeline/Csv/PositionsCsvParser.cs)
   feeds Step 1; nothing reads positions from a table.
3. **SendToBank lives in WPF.**
   [Step10PortfolioConstructorViewModel.cs](../FikaFinans.Wpf/ViewModels/Steps/Step10PortfolioConstructorViewModel.cs)
   maps trades to `TradingOrder` rows on a button click. The pipeline
   backend that
   [backend-nav-sync-plan.md](./backend-nav-sync-plan.md) plans for has
   no order-submission path of its own.

This document plans:

- A pluggable storage layer — **SQLite locally, Azure Tables in
  production, switched by config**.
- A first-class **Positions table** that replaces `positions.csv` and
  the existing `FundHolding` EF entity.
- Moving Step 10's order-submission logic out of WPF and into the
  daily Step 10 Function from
  [backend-nav-sync-plan.md](./backend-nav-sync-plan.md).
- The migration order — local SQLite first, then positions, then
  Step 10 rewiring, then the Azure Tables backend behind the same
  contract.

## Today vs target

```mermaid
flowchart LR
  subgraph today[Today]
    csv["positions.csv"] --> step1a["Step 1 DataLoader"]
    step1a --> stepN["Steps 2-9"]
    stepN --> step10a["Step 10 trades JSON"]
    step10a --> wpfa["WPF SendToBank button"]
    wpfa --> mem[("BankDbContext<br/>(in-memory)")]
  end
```

```mermaid
flowchart LR
  subgraph target[Target]
    pos[("Positions<br/>repository")] --> step1b["Step 1 DataLoader"]
    step1b --> stepNb["Steps 2-9"]
    stepNb --> step10b["Step 10 Function<br/>(daily timer)"]
    step10b --> ord[("Order<br/>repository")]
    step10b --> recon["reconcile<br/>positions"]
    recon --> pos
    cfg{"AppSettings<br/>Database.Provider"} -.-> repos["repository binding"]
    repos -.-> sqlite[("SQLite<br/>local")]
    repos -.-> tables[("Azure Tables<br/>prod")]
  end
```

## Storage abstraction

The contract that lets the two backends swap. Reuses the rules from
[backend-nav-sync-plan.md §Storage](./backend-nav-sync-plan.md#storage--azure-tables--local-sqlite-mirror)
with one deliberate divergence (no ETag — see §3.1).

- One repository interface per logical entity (Positions, Orders,
  per-ISIN progress, etc.).
- No FKs, no navigation properties, no joins, no `IQueryable` leaks
  across the contract boundary.
- `PartitionKey` and `RowKey` are real properties on every entity. Same
  POCO serializes to both stores.
- Tables-shaped writes only — batch ops scoped to a single partition,
  ≤100 entities, ≤4 MB. SQLite layer pretends to honour the same cap
  so behaviour matches in both modes.

The existing `BankDbContext` relies on EF features (cascade deletes,
FK relations on `Fund`/`NavSnapshot`/`Transaction`/`JournalEntry`)
that the new contract forbids. Those entities are reshaped, not
preserved.

### 3.1 Entity-shape gap — keys reshape, no ETag

Current EF entities use surrogate `Guid Id` keys
([AccountConfiguration.cs](../FikaFinans.Infrastructure/Bank/Persistence/Configurations/AccountConfiguration.cs)
and the other six configurations). Azure Tables wants `PartitionKey`
+ `RowKey`. The migration picks a natural two-part key for every
entity and retires the surrogate `Guid Id`.

**Decision: no ETag on any DTO.** Last-write-wins everywhere.

The Azure Tables service still maintains its internal ETag on every
row — we cannot turn that off and don't try to. What we control is
whether *our writes check it*: every Tables write passes the wildcard
`ETag.All`, so writes never gate on the prior value. SQLite/EF gets
no `[ConcurrencyCheck]` column, so its UPDATE statements use only
the PK in their `WHERE` clause. **Both backends end up identical:
reading is non-locking, writing always succeeds, latest write wins.**

Why this is safe here:

- `Account`, `TradingOrder`, `Positions`, `Transaction`,
  `JournalEntry` — single writer at a time (WPF user action, or the
  daily Step 10 Function, never both). No racing reads-then-writes.
- `IsinProgress` — even if two queue workers both claim the same
  ISIN, they would write the same thing (`State = Processing`) and
  run the same pipeline on the same inputs. The Step JSON columns
  the second worker writes match what the first wrote. Latest write
  is still correct.

We give up the ability to detect "someone else wrote this row
between my read and my write." No code path needs that signal today.

`Timestamp` is kept only where it has independent diagnostic value
(`Positions.LastUpdatedAt`, `TradingOrder.SubmittedAt`,
`IsinProgress.ProcessingStartedAt`). Not a universal property.

### 3.2 Per-entity key shapes

| Entity | `PartitionKey` | `RowKey` | Timestamp |
| --- | --- | --- | --- |
| `Account` | `"accounts"` (single-portfolio) | account `Code` (already unique) | — |
| `TradingOrder` | `"orders/{yyyy-MM-dd}"` | composite `(isin, side)` for daily idempotency | `SubmittedAt` |
| `Transaction` | `"ledger/{yyyy-MM}"` | `Guid` surrogate | service-stamped |
| `JournalEntry` | same partition as parent `Transaction` | `Guid` | — |
| `Positions` | `"positions"` | ISIN (or `"CASH"`) | `LastUpdatedAt` |
| `IsinProgress` | `"isin-progress"` | ISIN | `ProcessingStartedAt` |

### 3.3 What's lost from EF

Cascade deletes, navigation properties, LINQ joins, and lazy loading
all disappear at the contract boundary. Code today that does (e.g.)
`account.Transactions` via a navigation property has to switch to a
partition scan against `Transactions` keyed by account. Real
refactor, not a config change.

Why we accept the reshape: Azure Tables literally cannot honour
the EF features — no joins, no FKs, no cross-partition transactions.
If the local SQLite store relies on those features, behaviour
diverges between local and prod, which is exactly what the storage
abstraction exists to prevent.

## Schema map

| Today (EF, SQLite-backed) | Target | Notes |
| --- | --- | --- |
| `Account` | `Account` (kept, repo-fronted) ✅ | Bank-sim ledger root. Reshaped per §3.2. |
| `Fund` | dropped — Phase 5 leftover | Still live in EF: read by `PortfolioQueryService`, `TradingService` settle, `BankCsvImporter` seed, `SettlementEngine`. Pipeline reads fund metadata from YR endpoint per [backend-nav-sync-plan.md §Data Fetch](./backend-nav-sync-plan.md#data-fetch--yr-fund-endpoint). |
| `NavSnapshot` | dropped — Phase 5 leftover | Still live in EF as `Fund.NavHistory`. NAV history will flow through pipeline state, not a long-lived table. |
| `FundHolding` | replaced by `Positions` ✅ | Done 2026-05-10. EF `DbSet`, configuration, and domain type all deleted. |
| `TradingOrder` | `TradingOrder` (kept, repo-fronted) ✅ | Output of Step 10's SendToBank. Backend pluggable (Phase 6). |
| `Transaction`, `JournalEntry` | kept (bank-sim only) ✅ | Repo-fronted; cascade-FK nav prop replaced by two-reads + in-memory join in `LedgerService`. |
| _(none)_ | `IsinProgress` | New. Per-ISIN row from [backend-nav-sync-plan.md §Progress Table](./backend-nav-sync-plan.md#progress-table--per-isin-state) — state + Step01Json…Step09Json + RunId. Phase 7. |
| _(none)_ | `PortfolioTrades` | New. Step 10 daily output. PK/RK shape an open question — see §10. Phase 4. |

## Positions table

The headline addition. Replaces `positions.csv`.

### 5.1 Shape

Per-ISIN holdings + a single Cash pseudo-row. Same partition,
distinguished by `RowKey`.

| Column | Notes |
| --- | --- |
| `PartitionKey` | constant `"positions"` (single-portfolio assumption) |
| `RowKey` | ISIN; `"CASH"` for the cash pseudo-row |
| `Isin` | mirrors `RowKey` for non-cash rows; null/empty on Cash |
| `Name` | display name; `"Cash"` on the cash row |
| `CurrentValueKr` | required |
| `CostBasisKr` | required for fund rows; equals current value on the cash row by convention |
| `LastUpdatedAt` | timestamp of the last reconciliation |
| `Source` | `"manual"` / `"sendToBank"` / `"reconciled"` — provenance of the latest write |

The Cash row keeps its semantics from
[PositionsCsvParser.cs:24-35](../FikaFinans.Infrastructure/Pipeline/Csv/PositionsCsvParser.cs#L24)
— at most one, value carried as `cash_available_kr` into Step 1.

### 5.2 Lifecycle

```mermaid
flowchart LR
  s1["Step 1 DataLoader"] -->|read all| pos[("Positions")]
  s10r["Step 10 PortfolioConstructor"] -->|read all| pos
  send["SendToBank"] -->|write per ISIN| pos
  wpf["WPF user edits"] -->|write per ISIN| pos
  recon["post-trade reconciliation"] -->|write| pos
```

- **Step 1 (DataLoader)** reads the partition. Replaces the CSV
  parse. Internal data shape exposed to downstream agents stays the
  same.
- **Step 10 (PortfolioConstructor)** reads positions for current-value
  math when sizing trades. Today this flows transitively through Step
  9's enriched output per
  [pipeline-plan.md §4.10](../FikaFinans.InfrastructureV2.Tests/docs/pipeline-plan.md);
  Step 10 also reads the table directly for authoritative current
  values when needed.
- **SendToBank** writes the table after orders are submitted (post-trade
  reconciliation). Reconciliation trigger — synchronous after order
  ack vs. event callback from the bank stub — is an open question.
- **WPF user edits** write through the same repository when the active
  binding is SQLite.

### 5.3 Why a separate partition, not the per-ISIN progress row

- **Different lifecycles.** The progress row is cleared at run start
  (per [backend-nav-sync-plan.md §"Run boundary"](./backend-nav-sync-plan.md#run-boundary)).
  Positions must survive across runs.
- **Different writers.** The progress row is owned by pipeline
  Functions. Positions are owned by SendToBank + manual user edits.
- **Different read shapes.** Step 1 wants *all* positions in one
  partition scan. Progress-row reads are PK lookups per ISIN.

### 5.4 The table is the canonical input — CSV stops being a runtime concept ✅ Done — 2026-05-10

**Both Azure (Step 1 in the Function) and WPF read positions
exclusively from the table.** No code path resolves them through a
CSV file at runtime. The CSV format is not a parallel input, not a
fallback, not an emergency read.

Where CSV-shaped data still appears, it is generated *from* the
table on the fly:

- **Diagnostic exports.** A WPF "export current positions" action
  serializes the table to CSV for human inspection. One-way; reads
  from the table, never written back.
- **Internal conversions inside the pipeline.** If any agent's
  internal contract still wants a CSV-shaped DTO
  (`PositionsParseResult` today), an in-memory adapter projects table
  rows into that shape at the Step 1 boundary. The CSV-flavoured DTO
  becomes an internal data-transfer detail, not a file format.

**Test fixtures.**
[FikaFinans.InfrastructureV2.Tests](../FikaFinans.InfrastructureV2.Tests/)
today loads `docs/inputs/positions.csv`. The test setup migrates:
tests seed an in-memory positions repository directly via small
builder helpers (or, transitionally, via a CSV-to-repository adapter
that lives in test-only code). Existing `positions.csv` fixtures may
stay as historical references but are no longer wired into the
runtime path.

**Production seed strategy.** First production run has an empty
positions table. The first SendToBank cycle populates it; or, if
we choose, an admin endpoint accepts a one-shot bulk-write call to
prime it. There is no "import positions.csv" runtime path.

**Net effect.**
[PositionsCsvParser.cs](../FikaFinans.Infrastructure/Pipeline/Csv/PositionsCsvParser.cs)
survives — used by
[BankCsvImporter](../FikaFinans.Infrastructure/Bank/BankCsvImporter.cs)'s
one-shot seed, by the test-fixture
`InMemoryPositionsRepository.SeededFromCsv` adapter, and by the
`PositionsCsvParserTests` unit tests. It just has no runtime caller
from the agent pipeline anymore. A new
[PositionsCsvWriter](../FikaFinans.Infrastructure/Pipeline/Csv/PositionsCsvWriter.cs)
mirrors its column shape for the WPF diagnostic export.

## Step 10 + SendToBank rewiring

What moves and where.

### 6.1 Today

```mermaid
flowchart LR
  user["WPF user clicks 'Send'"] --> vm["Step10ViewModel"]
  vm -->|"GetFundPositionsAsync()"| fh[("FundHolding<br/>in-memory")]
  vm -->|"CreateBuyOrderAsync /<br/>CreateSellOrderAsync"| order[("TradingOrder<br/>in-memory")]
```

Manual-only. Backend: in-memory `BankDbContext`. State evaporates on
exit.

### 6.2 Target

```mermaid
flowchart LR
  timer["Daily timer<br/>23:00 CET"] --> fn["Step 10 Function"]
  fn -->|"read all"| pos[("Positions repository")]
  fn -->|"read Step09Json per ISIN"| isin[("IsinProgress repository")]
  fn -->|"write daily orders"| ord[("TradingOrder repository")]
  fn -->|"reconcile after ack"| pos
  cfg{"AppSettings.Database.Provider"} -.-> pos
  cfg -.-> ord
  wpf["WPF read-only view"] -.-> pos
  wpf -.-> ord
```

Step 10 Function (daily timer per
[backend-nav-sync-plan.md §"Step 10 — Daily Portfolio Trades"](./backend-nav-sync-plan.md#step-10--daily-portfolio-trades))
owns the path from Step 09 output to submitted order. WPF becomes
display-only by default. Whether a manual "send" trigger still exists
in WPF for ad-hoc local runs is an open question (see §10).

### 6.3 Idempotency and re-runs

Step 10 is idempotent at the day level (per backend-nav-sync-plan.md).
Re-running on the same day overwrites the same `TradingOrder` rows
rather than creating duplicates. The `(date, isin, side)` `RowKey`
shape from §3.2 enforces this — a second run hits the same row and
overwrites it.

### 6.4 Local vs prod, switched by config

Same Function logic. DI swap selects the SQLite or Azure Tables
binding for each repository. The config knob is
`AppSettings.Database.Provider` — already exists at
[AppSettings.cs](../FikaFinans.Application/Settings/AppSettings.cs)
with default `"InMemory"` today; new values `"Sqlite"` and
`"AzureTables"` join the enum and `"InMemory"` retires once the new
path is in.

## Configuration switching

- `AppSettings.Database.Provider` ∈ `{"Sqlite", "AzureTables"}`.
- For SQLite: `AppSettings.Database.Path` is the file path. Schema
  applied at startup via `EnsureCreated`-style check; no migrations
  initially. "Introduce EF migrations once the schema stabilises" is
  a follow-up.
- For Azure Tables: storage-account connection from managed identity
  per
  [backend-nav-sync-plan.md §Infrastructure Summary](./backend-nav-sync-plan.md#infrastructure-summary).
- Module wiring lives in
  [InfrastructureModule.cs](../FikaFinans.Infrastructure/DependencyInjection/InfrastructureModule.cs).
  One binding per repository per provider.

## Migration phases

Each phase is independently mergeable and testable. Only ordering
constraint: Phase 4 needs Phase 3.

1. **Stand up real SQLite locally.** ✅ **Done — 2026-05-10.**
   Replaced `UseInMemoryDatabase` with `UseSqlite` against
   `%USERPROFILE%\Documents\FikaFinans\fikafinans.db` (configurable via
   `AppSettings.Database.Path`). All seven `BankDbContext` consumers now
   take an `IDbContextFactory<BankDbContext>` and open a fresh context per
   public method — fixes the singleton-DbContext bug that was unsafe
   under real SQLite. Schema setup via `EnsureCreatedAsync()` in
   `DataSeeder.SeedAsync()`. The existing 7 tables stay as-is — this
   phase is purely "stop losing state on exit." `Provider` setting now
   accepts `Sqlite` (default), `InMemory` (kept for tests), or
   `AzureTables` (placeholder, throws — wired up in Phase 6).
2. **Introduce the repository abstraction.** ✅ **Done — 2026-05-10.**
   Five Tables-shaped repo interfaces under
   [FikaFinans.Application/Storage/Bank](../FikaFinans.Application/Storage/Bank/)
   (`IAccountsRepository`, `ITradingOrdersRepository`,
   `ITransactionsRepository`, `IJournalEntriesRepository`,
   `IPositionsRepository`) plus POCO row entities under
   `…/Storage/Bank/Entities/`. SQLite implementations under
   [FikaFinans.Infrastructure/Storage/Sqlite](../FikaFinans.Infrastructure/Storage/Sqlite/);
   each opens a fresh `BankDbContext` per public method via the
   factory from Phase 1. Bank-sim consumers (`TradingService`,
   `LedgerService`, `PortfolioQueryService`,
   [BankCsvImporter](../FikaFinans.Infrastructure/Bank/BankCsvImporter.cs))
   read/write through the interfaces. `Transaction.Entries` cascade-FK
   nav prop dropped in favour of a two-reads + in-memory join in
   `LedgerService`. **Consumers that touch `Fund`/`NavSnapshot` still
   hold an `IDbContextFactory<BankDbContext>`** — closing that gap is
   tracked under Phase 5 below.
3. **Add the Positions table; switch Step 1 onto it.** ✅ **Done — 2026-05-10.**
   New
   [PositionRow](../FikaFinans.Infrastructure/Storage/Sqlite/Entities/PositionRow.cs)
   table backs
   [IPositionsRepository](../FikaFinans.Application/Storage/Bank/IPositionsRepository.cs)
   (`PartitionKey = "positions"`; `RowKey = ISIN | "CASH"`). Schema
   carries `Units` + `AvgCostPerUnit` beyond the CSV shape — needed
   for the bank-sim's unit-based sell flow per the chunk-5 design
   note. [DataLoaderAgent](../FikaFinans.Infrastructure/Pipeline/Agents/DataLoaderAgent.cs)
   reads from the repo via an internal `ToPositionsParseResult`
   adapter; the CSV-flavoured `PositionsParseResult` DTO survives only
   as an in-process shape between the adapter and `Join(...)`.
   [BankCsvImporter](../FikaFinans.Infrastructure/Bank/BankCsvImporter.cs)
   is now a one-shot seed — first run reads `positions.csv` and
   upserts; subsequent runs short-circuit. WPF gained a one-way
   "Export Positions CSV" diagnostic that writes
   `%USERPROFILE%\Documents\FikaFinans\exports\positions-{yyyy}-W{ww}.csv`
   via a new
   [PositionsCsvWriter](../FikaFinans.Infrastructure/Pipeline/Csv/PositionsCsvWriter.cs).
   `positions.csv` is no longer a runtime read path — only a seed +
   test-fixture input + diagnostic output, exactly per §5.4.
4. **Move SendToBank into the Step 10 Function.** **Not started.**
   Blocked on the queue-driven backend from
   [backend-nav-sync-plan.md](./backend-nav-sync-plan.md) existing.
   Function reads Positions, writes `TradingOrder`. WPF becomes the
   read-only view (or keeps a manual trigger — open question in §10).
5. **Drop `Fund`, `NavSnapshot`, `FundHolding`** once nothing reads
   them. 🟡 **Partial — `FundHolding` retired 2026-05-10.** The EF
   `DbSet<FundHolding>`, its configuration, the `FundHolding` domain
   type, and `FundHoldingId` are all gone. `Fund` + `NavSnapshot`
   still alive in EF: `PortfolioQueryService.GetFundPositionsAsync`
   reads `db.Funds.Include(f => f.NavHistory)` for NAV + display name,
   `TradingService.SettleBuyOrderAsync` reads `db.Funds` to get
   `Isin`/`Name` for the holdings-account creation, `BankCsvImporter`
   creates `Fund` records on first seed, and `SettlementEngine` reads
   `db.Funds.Include(f => f.NavHistory)` per pending order. Closing
   this out needs a Funds repo (with NAV history attached or split)
   so those four consumers don't hold the EF context — see open
   question in §10.
6. **Add the Azure Tables implementation behind each repository
   interface.** **Not started.** Five `AzureTables*Repository` classes
   that target Azurite locally / Azure Tables in prod. DI swap by
   config (`AppSettings.Database.Provider = "AzureTables"` — already
   accepted as a value, currently throws `NotImplementedException`
   in
   [InfrastructureModule](../FikaFinans.Infrastructure/DependencyInjection/InfrastructureModule.cs)).
   SQLite stays as the local-dev default.
7. **Per-ISIN progress row + step JSON columns** land in the same
   table-fronted contract as the rest of the data. **Not started —**
   gated on Phase 4. This is when
   [backend-nav-sync-plan.md](./backend-nav-sync-plan.md)'s storage
   section becomes real.

## Test strategy

- **Unit tests** stay against the repository interface — the same
  test hits both backends (Tables hits Azurite locally; the existing
  dev environment already has Azurite per the repo
  [CLAUDE.md](../CLAUDE.md)).
- **InfrastructureV2.Tests fixtures move to repository seeding.**
  Today's tests under
  [FikaFinans.InfrastructureV2.Tests/Agents/01-dataloader](../FikaFinans.InfrastructureV2.Tests/Agents/01-dataloader/)
  load `docs/inputs/positions.csv`. The new setup populates an
  in-memory positions repository via small builder helpers (or,
  transitionally, a CSV-to-repository adapter in test-only code).
  The runtime path never sees a CSV regardless.
- **A round-trip test for SendToBank** asserts: trade computed →
  TradingOrder written → positions reconciled → second run on the
  same day produces no duplicate orders.

## Open questions

- **Manual SendToBank trigger in WPF** — kept or removed? Currently
  kept; the WPF Bank tab still has its create-buy/sell/settle buttons
  against the local bank-sim. Phase 4 decides whether they stay
  alongside the daily Function or get removed in favour of a
  read-only WPF view.
- **`TradingOrder` `RowKey` exact form.** ✅ **Resolved 2026-05-10.**
  Composite `"{isin}/{side}"` per §3.2; second submission on the same
  ISIN+side+day overwrites the first (last-write-wins). PartialSell
  scenarios that need two same-day same-side trades will need a
  sequence suffix when they materialise — not a problem today.
- **Reconciliation trigger** — synchronous after ack vs event
  callback from the bank stub. Still open. The current bank-sim
  settlement reconciliation runs on the night-tick from `BankSimulator`
  per [SettlementEngine.cs](../FikaFinans.Infrastructure/Bank/SettlementEngine.cs);
  Phase 4 picks the production shape.
- **Cash row representation** — ✅ **Resolved 2026-05-10.**
  `RowKey = "CASH"` chosen; same partition as the per-ISIN rows.
  Mirrored across the SQLite-backed `Positions` table and the
  test-side `InMemoryPositionsRepository`.
- **Position schema beyond the CSV shape** — ✅ **Resolved 2026-05-10.**
  Added `Units` + `AvgCostPerUnit` (precision `decimal(18, 6)`) so
  the bank-sim's unit-based sell flow keeps its "sell N units; cost
  basis = N × AvgCostPerUnit" semantics without round-tripping
  through NAV. The CSV stays value-only; the extra columns ride on
  the row but aren't surfaced through the diagnostic export.
- **SQLite schema-evolution strategy** — `EnsureCreated` initially;
  proper EF migrations later. Still open. Today's seam: deleting
  the `.db` file and letting `EnsureCreated` rebuild it. Acceptable
  while there's no production data.
- **Storage-account split.** Whether `Account` /
  `Transaction` / `JournalEntry` (the bank-sim ledger) belong in the
  same storage account as the pipeline state, or in a separate one
  for blast-radius reasons. Still open. Decided at Phase 6.
- **`PortfolioTrades` PK/RK shape.** Single daily row vs per-ISIN
  column — already tracked in
  [backend-nav-sync-plan.md §"Step 10 — Daily Portfolio Trades"](./backend-nav-sync-plan.md#step-10--daily-portfolio-trades).
  Decided at Phase 4.
- **Funds repo shape** — new question, surfaced by Phase 5 leftovers.
  Whether `Fund` + its `NavHistory` collection split into two repos
  (funds + nav snapshots) or stay as a single aggregate. Today's
  consumers all want them together (NAV-by-ISIN reads), so a single
  repo is the lean answer.

## Out of scope

- The wire format of orders sent to a real bank/broker. Today's
  `ITradingService` is a local stub; replacing it with a real
  integration is a separate doc.
- Backups and disaster recovery for either the SQLite file or the
  Azure Tables data.
- WPF UI changes beyond "this view becomes read-only" — visual
  design is out of scope.
- Any code, ARM/Bicep, or DI snippets — same rule as
  [backend-nav-sync-plan.md](./backend-nav-sync-plan.md).
