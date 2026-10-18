<!--
  Authoring rules for AI assistants and humans editing anything in this
  folder — inherited from the parent plan:
  - DO NOT write code (no C#, no XAML, no JSON config snippets, no shell).
    ONE exception: a short illustrative sketch showing how a seam looks in
    each host. Keep it under ~10 lines, label it a sketch, and mark names
    that do not exist yet as (new).
  - DO use Mermaid diagrams to express architecture, flows, and state.
  - Prose stays at the "what / why / where it lives" level — no API
    signatures, no method bodies. Class names already in the codebase may
    be referenced; do not invent new ones as if they existed.
  - Cross-references are one-way: link out to other docs, never edit those
    other docs to point back here.
  - DO NOT invent architecture. If something is not yet decided, write it
    as an open question, not as a confident design.
-->

# Pipeline Redesign

**This folder started as a design.** Steps 1 to 7 are now implemented
for the local (WPF) host — each page's status line says how far; every
other page is still design only, and no page is a record of current
behaviour beyond what its status says.

One file per pipeline step, describing how that step changes to become
signal-driven, per-fund, and hostable outside the desktop app — so the
same logic can run locally on an Rx stream and in cloud on a queue
trigger without being rewritten.

## What owns what

| Document | Describes |
| --- | --- |
| [event-driven-orchestration-plan.md](./event-driven-orchestration-plan.md) | The cross-cutting design — the commands-down/events-up rule, the fetch seam, transport constraints, terminology, blockers and open questions |
| **This folder** | Per-step consequences of that design — one file per step |
| [`FikaFinans.InfrastructureV2.Tests/docs/NN-*.md`](../../FikaFinans.InfrastructureV2.Tests/docs/) | Each step's **current** contract: I/O schemas, failure modes, test fixtures. Still authoritative for what a step does today. |

Files here use the same number-and-slug naming as the existing contracts
so the pair is obvious. A step's file here says what *changes*; its
contract file says what the step *is*. Neither replaces the other, and
nothing in this folder edits the contracts.

Cross-step topics that do not belong to any single step — the
universe-wide barrier steps and how they translate to a queue chain, for
instance — can live here as their own file rather than being forced into
a step's page.

## Template

Every step file uses the same four sections, so they can be read side by
side and diffed against each other:

| Section | Answers |
| --- | --- |
| **Input trigger** | What causes this step to run, in both environments |
| **Input data and source** | What it reads, and where that comes from before and after |
| **What processing is done** | The work itself, and any constraint that per-fund processing changes |
| **Output, and what triggers the next step** | What is written, where, with what lifetime — and what signal is emitted |

Anything undecided goes in the parent plan's Open Questions, not
resolved locally. A step file may state a constraint the parent has
already settled; it may not settle one on its own.

## Steps

| # | Step | Redesign notes |
| --- | --- | --- |
| 1 | DataLoader | [01-dataloader.md](./01-dataloader.md) |
| 2 | MetricsCalculator | [02-metricscalculator.md](./02-metricscalculator.md) |
| 3 | MacroAnalyst | [03-macroanalyst.md](./03-macroanalyst.md) — implemented locally; a per-fund hop doing per-week work |
| 4 | SignalScorer | [04-signalscorer.md](./04-signalscorer.md) — implemented locally; a code-only hop, step 2's pattern |
| 5 | MacroAligner | [05-macroaligner.md](./05-macroaligner.md) — implemented locally; two inputs, step 4's record and step 3's context; the chain tail |
| 6 | CatalystTagger | [06-catalysttagger.md](./06-catalysttagger.md) — implemented locally; step 5's shape with catalysts, but the LLM runs for every fund |
| 7 | ThesisValidator | [07-thesisvalidator.md](./07-thesisvalidator.md) — implemented locally; one input, no context; hybrid like step 5 — the LLM only on conflicting matrix rows |
| 8 | Recommender | [08-recommender.md](./08-recommender.md) — design; code-only and synchronous, step 4's agent call on step 7's chain; the last per-fund link and the chain tail |
| 9 | UniverseEnricher | not written — universe-wide barrier, translation unresolved |
| 10 | PortfolioConstructor | not written — runs on a daily timer, outside the per-fund chain |
