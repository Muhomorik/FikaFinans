# CLAUDE.md — FikaFinans.InfrastructureV2.Tests

> Folder-local instructions. The repo-root [CLAUDE.md](../CLAUDE.md) still applies
> (tone, naming, MAF/Foundry knowledge-freshness warning, etc.) — this file only
> adds rules specific to **InfrastructureV2.Tests**.

## What this project is

The **canonical Infrastructure test project** for FikaFinans. It tests the
10-agent fund-rotation pipeline that lives in [FikaFinans.Infrastructure](../FikaFinans.Infrastructure/)
and the domain/application contracts in [FikaFinans.Domain](../FikaFinans.Domain/)
and [FikaFinans.Application](../FikaFinans.Application/).

The pipeline turns weekly fund data and macro narrative into executable trade
decisions across 10 agents (01 → 10). The navigator below is auto-loaded into
every session; deeper specs are read on demand.

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

1. **Test Infrastructure types directly.** This project references
   `FikaFinans.Domain`, `FikaFinans.Application`, and `FikaFinans.Infrastructure`
   as `<ProjectReference>`s — use their types directly, never copy them locally.
2. **Test fixtures stay local.** Hardcoded fixture data (CSV inputs, JSON
   examples) and the `[CallerFilePath]`-based `Paths.cs` stay in this project.
   Production code uses `IPathsService` from Application instead.
3. **TDD always.** Write the failing test first, watch it go red, *then* write
   the production code in Infrastructure to turn it green. No "I'll add tests later."

## 📚 Tooling conventions

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
