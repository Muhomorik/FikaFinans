# CLAUDE.md — FikaFinans.InfrastructureV2.Tests

> Folder-local instructions. The repo-root [CLAUDE.md](../CLAUDE.md) still applies
> (tone, naming, MAF/Foundry knowledge-freshness warning, etc.) — this file only
> adds rules specific to **InfrastructureV2.Tests**.

## What this project is

A **greenfield, TDD-only sandbox** for completely new infrastructure features.
It is *not* a V2 rewrite of [FikaFinans.Infrastructure](../FikaFinans.Infrastructure/) —
it is a clean room where new ideas get written test-first, in isolation, before
they ever earn a place in the main projects.

The current focus is the **fund-rotation pipeline** — 10 agents (01 → 10) that
turn weekly fund data and macro narrative into executable trade decisions. The
navigator below is auto-loaded into every session; deeper specs are read on
demand.

@docs/instructions.md

## Project layout

- `Agents/` — agent implementations, co-located with their tests (TDD)
- `Models/` — DTOs, contracts, enums shared across agents
- `docs/` — design docs, agent contracts, configs, sample inputs/outputs
- `docs/inputs/` — hardcoded sample data; treat as fixtures, do not modify
- `docs/examples/` — reference outputs from prior chain runs
- `stepOutputs/` — generated artifacts; safe to delete; should be gitignored
- `Paths.cs` — centralized hardcoded paths (resolve repo root via `[CallerFilePath]`)

## Reference docs (read on demand — do not auto-load)

- `docs/pipeline-plan.md` — full architecture, all 10 agent contracts,
  vocabularies, invariants, glossary (~1.2k lines; way too big to inline)
- `docs/01-dataloader.md` … `docs/10-portfolioconstructor.md` — per-agent
  contracts; read the one you're implementing
- `docs/config-{02,04,09,10}-*.json` — agent configurations consumed at runtime
- `docs/inputs/analytics-json-schema.md`, `summary-csv-plan.md`,
  `snapshot-csv-plan.md` — input schemas (JSON + the two YieldRaccoon CSVs)
- `docs/inputs/portfolio_structure.md`,
  `docs/inputs/FUND-STATISTICS-EXPORT-AGENT-GUIDE.md` — layer definitions and
  CSV reference
- `docs/signals-chart-spec.md`, `docs/signals_interactive.html` — signals
  visualization spec + interactive viewer

## Pipeline conventions

- **Implement agents in order 01 → 10.** Each step's output is the next
  step's input — implement and validate sequentially.
- **Append-only chain.** Never mutate fields produced by earlier agents;
  only add new fields.
- **Step 10 is strictly deterministic (no LLM).** Required so backtests
  replay byte-identically.
- **Hardcoded paths live in `Paths.cs`** and resolve via `[CallerFilePath]`
  so tests work from any working directory.

## 🚦 Hard rules

1. **New functionality only.** Nothing in here should reproduce, port, or extend
   code that already lives in [FikaFinans.Infrastructure](../FikaFinans.Infrastructure/)
   or [FikaFinans.Infrastructure.Tests](../FikaFinans.Infrastructure.Tests/).
   If a feature already has a home, work there instead.
2. **Stay isolated.** No edits to other FikaFinans projects (`Domain`,
   `Application`, `Infrastructure`, `Wpf`, or their test projects), and no
   `<ProjectReference>` to them either. If you need a class from an existing
   project, **copy it locally** into this project (with a comment noting the
   source) rather than referencing it. This project must compile and pass even
   if every other project in the solution were deleted from the working tree.
3. **TDD always.** Write the failing test first, watch it go red, *then* write
   the production code (which also lives in this project for now) to turn it
   green. No "I'll add tests later."

## 📚 Reference implementation

When tooling, fixtures, AutoFixture/AutoMoq wiring, user-secrets, or NUnit
conventions are unclear, check
[FikaFinans.Infrastructure.Tests.csproj](../FikaFinans.Infrastructure.Tests/FikaFinans.Infrastructure.Tests.csproj)
and the test base classes in that project (e.g.
[FundPipelineSandboxTestBase.cs](../FikaFinans.Infrastructure.Tests/FundPipelineSandbox/Tests/FundPipelineSandboxTestBase.cs)) —
they are the **known-good reference** for how this stack likes to be set up.
Mirror their patterns; do not reference them.

Notable conventions worth copying when you get there:

- **.NET 9** target, **NUnit 4.x**, NUnit.Analyzers 4.x.
- **AutoFixture + AutoFixture.AutoMoq + Moq** for SUT resolution
  (per [dotnet-unit-testing-nunit](../.claude/skills/dotnet-unit-testing-nunit/)
  skill — always resolve the SUT from the fixture, never `new` it).
- **Microsoft.Extensions.Configuration.UserSecrets** with a project-specific
  `<UserSecretsId>` for live Foundry endpoints / API keys. **All keys and
  endpoints live in user-secrets — never in `appsettings*.json`, `.env`,
  source, or test fixtures.** Read them via `ConfigurationBuilder().AddUserSecrets<T>()`
  and `Assert.Ignore(...)` if a required secret is missing locally.
- `<None Include="...\**\*.*" CopyToOutputDirectory="PreserveNewest" />` for
  prompt folders, fixture data, etc.
- `Assert.Ignore("…")` (not `Assert.Fail`) when an integration test can't run
  because a secret is missing — keeps CI green.
