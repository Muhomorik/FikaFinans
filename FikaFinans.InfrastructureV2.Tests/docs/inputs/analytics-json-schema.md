# KanelBrief Weekly Analytics — JSON Schema

This document specifies the JSON schema for the three weekly analytics reports
produced by the KanelBrief market-intelligence pipeline. It is intended as
**tool context** for AI agents that consume these JSON files in downstream
projects (fund analytics, portfolio matching, dashboard rendering, etc.).

## What these files represent

A four-step LLM pipeline runs on a schedule (daily news brief, weekly summary
on Thursdays, then substitution chain and opportunity scan chained from the
weekly). The three weekly reports build on each other:

```text
NewsBriefRun (daily, ~6×/day)        ← out of scope of this schema
        ↓ aggregated weekly
WeeklySummaryRun                     ← analytics-weekly-summary.json
        ↓ derived
SubstitutionChainRun                 ← analytics-substitution-chain.json
        ↓ derived
OpportunityScanRun                   ← analytics-rotation-targets.json
```

Each report covers **one period** (currently a week, identified by an ISO 8601
week tag). The bounds are inclusive-start, exclusive-end — the producing
system decides exactly which dates fall inside; do not assume any specific
weekday alignment. The three reports for a given period share the same
`periodStart`, `periodEnd`, and `periodIsoWeek`.

## Conventions

- **JSON keys** are `camelCase`.
- **Dates** are ISO 8601 with offset, e.g. `"2026-04-20T00:00:00+00:00"`.
  Parse them as `DateTimeOffset` / `Date`. Period-edge values use UTC.
- **`runDate`** is a calendar-date string `"yyyy-MM-dd"` representing the day
  the run executed (UTC), not the period it covers.
- **`runId`** is a GUID string. Cross-references between reports use these IDs.
- **Enums** are emitted as their string member names (see § Enums below).
- All three reports include the **same period fields** (`periodStart`,
  `periodEnd`, `periodIsoWeek`) — denormalized so each file is self-describing
  and consumable in isolation, without joining to its parent report.

## Common base fields (present in all three reports)

| Field             | Type                | Description                                                        |
| ----------------- | ------------------- | ------------------------------------------------------------------ |
| `runId`           | string (GUID)       | Unique identifier of this run.                                     |
| `runDate`         | string `yyyy-MM-dd` | Date the agent was executed (UTC).                                 |
| `createdAt`       | string ISO 8601     | Exact timestamp when the run started.                              |
| `modelId`         | string              | LLM model used (e.g. `"gpt-5.4-mini"`).                            |
| `status`          | enum `RunStatus`    | `"Success"` / `"Partial"` / `"Failed"`.                            |
| `durationSeconds` | number              | Wall-clock duration of the agent call.                             |
| `inputTokens`     | integer             | LLM prompt tokens consumed.                                        |
| `outputTokens`    | integer             | LLM completion tokens produced.                                    |
| `totalTokens`     | integer             | `inputTokens + outputTokens`.                                      |
| `reportType`      | string discriminator | `"weekly-summary"` / `"substitution-chain"` / `"rotation-targets"`. |
| `periodStart`     | string ISO 8601     | Inclusive start of the analyzed period.                            |
| `periodEnd`       | string ISO 8601     | Exclusive end of the analyzed period.                              |
| `periodIsoWeek`   | string `YYYY-Www`   | ISO 8601 week tag of `periodStart`, e.g. `"2026-W17"`.             |

> Use `reportType` to dispatch on file shape when you receive a generic JSON
> payload — it is set even when filename is unavailable.

## Report shapes

### `analytics-weekly-summary.json` — `WeeklySummaryRun`

Aggregates 5–7 daily news briefs into 2–4 confidence-weighted recurring themes
plus a net mood for the week.

| Field         | Type                       | Description                                                      |
| ------------- | -------------------------- | ---------------------------------------------------------------- |
| `netMood`     | enum `MarketSentiment`     | Dominant sentiment across the week's daily briefs.               |
| `moodSummary` | string                     | 1–2 sentence narrative of the week's arc.                        |
| `themes`      | `WeeklySummaryTheme[]`     | 2–3 recurring themes; each tied to ≥2 daily briefs.              |

`reportType` is always `"weekly-summary"`.

#### `WeeklySummaryTheme`

| Field        | Type                   | Description                                                                                    |
| ------------ | ---------------------- | ---------------------------------------------------------------------------------------------- |
| `category`   | string                 | Short theme name (free-form, e.g. `"AI and semiconductor leadership"`).                        |
| `summary`    | string                 | What recurred and how widely (1–2 sentences).                                                  |
| `confidence` | enum `ConfidenceLevel` | `"High"` (≥5 daily briefs reinforce), `"Medium"` (3–4), `"Low"` (exactly 2). Themes with <2 are dropped. |
| `sentiment`  | enum `MarketSentiment` | The theme's own trajectory across the week — **not** the overall market mood.                  |

### `analytics-substitution-chain.json` — `SubstitutionChainRun`

Identifies capital rotation chains derived from the weekly summary's themes.
Each chain says: capital is fleeing X, flowing toward Y, because of mechanism Z.

| Field                 | Type            | Description                                                              |
| --------------------- | --------------- | ------------------------------------------------------------------------ |
| `weeklySummaryRunId`  | string (GUID)   | FK to the parent `WeeklySummaryRun`.                                     |
| `chains`              | `RotationChain[]` | 2–4 rotation chains grounded in the input themes.                       |

`reportType` is always `"substitution-chain"`.

#### `RotationChain`

| Field            | Type   | Description                                                                                          |
| ---------------- | ------ | ---------------------------------------------------------------------------------------------------- |
| `capitalFleeing` | string | Sector / asset class losing capital.                                                                 |
| `flowsToward`    | string | Sector / asset class gaining capital. **Must be different from `capitalFleeing`.**                   |
| `mechanism`      | string | Why the rotation occurred — references a specific theme name from the parent weekly summary.        |

### `analytics-rotation-targets.json` — `OpportunityScanRun`

Scores 2–4 actionable rotation targets, grounded in the chains. Includes a
contrarian "Weak" tier for selective bets on reversal.

| Field                     | Type              | Description                                                       |
| ------------------------- | ----------------- | ----------------------------------------------------------------- |
| `substitutionChainRunId`  | string (GUID)     | FK to the parent `SubstitutionChainRun`.                          |
| `targets`                 | `RotationTarget[]` | 2–4 targets backed by chains.                                    |

`reportType` is always `"rotation-targets"`.

#### `RotationTarget`

| Field            | Type                  | Description                                                                                                           |
| ---------------- | --------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `category`       | string                | Asset or sector worth watching.                                                                                       |
| `signalStrength` | enum `SignalStrength` | `"Strong"` (≥2 chains point the same way), `"Moderate"` (1 chain), `"Weak"` (contrarian — target is on the fleeing side). |
| `rationale`      | string                | Why this is an opportunity — cites the specific chain(s) supporting it.                                               |
| `riskCaveat`     | string                | Specific catalyst or chain reversal that would invalidate this target.                                                |

## Enums

### `RunStatus`

| Value       | Meaning                                                                            |
| ----------- | ---------------------------------------------------------------------------------- |
| `Success`   | Agent completed and output was parsed cleanly.                                     |
| `Partial`   | Agent completed but parser flagged warnings (some content was skipped). Treat data as usable but degraded. |
| `Failed`    | Agent or parser threw; payload arrays may be empty. Do not consume content.       |

### `MarketSentiment`

| Value      | Meaning                                                                                  |
| ---------- | ---------------------------------------------------------------------------------------- |
| `RiskOn`   | Investors are taking risk — equities up, credit spreads tight, defensives lag.           |
| `RiskOff`  | Investors are de-risking — bonds and cash bid, equities and high-beta sectors weak.      |
| `Mixed`    | Conflicting signals across sectors and assets — no clean directional read.               |

### `ConfidenceLevel` (theme strength in `WeeklySummaryRun.themes`)

| Value    | Meaning                                                                            |
| -------- | ---------------------------------------------------------------------------------- |
| `High`   | Theme reinforced by 5 or more daily briefs in the week.                            |
| `Medium` | Reinforced by 3–4 daily briefs.                                                    |
| `Low`    | Reinforced by exactly 2 daily briefs (the minimum bar for inclusion).              |

### `SignalStrength` (target strength in `OpportunityScanRun.targets`)

| Value      | Meaning                                                                                            |
| ---------- | -------------------------------------------------------------------------------------------------- |
| `Strong`   | Target is supported by ≥2 chains pointing the same direction.                                      |
| `Moderate` | Target is supported by exactly 1 chain.                                                            |
| `Weak`     | **Contrarian play** — target is on the `capitalFleeing` side of a chain; bet on reversal.          |

## Cross-file relationships

The three reports for the same week form a chain by GUID reference:

```text
WeeklySummaryRun.runId
        ↑
        │ weeklySummaryRunId
        │
SubstitutionChainRun.runId
        ↑
        │ substitutionChainRunId
        │
OpportunityScanRun.runId
```

If you need to verify three files belong to the same week without following
the FK chain, compare `periodIsoWeek` — it is identical across all three.

## Example payloads

See the sibling files in this folder:

- [`analytics-weekly-summary.json`](analytics-weekly-summary.json)
- [`analytics-substitution-chain.json`](analytics-substitution-chain.json)
- [`analytics-rotation-targets.json`](analytics-rotation-targets.json)

All three are real production outputs for ISO week 2026-W17 (Apr 20 – Apr 27, 2026).

## Notes for downstream consumers

- **Empty arrays are valid.** A `RunStatus.Success` weekly summary may have
  `themes: []` if no theme cleared the 2-brief reinforcement bar. Same for
  chains and targets. Don't treat empty as an error.
- **String fields are free-form prose** — `category`, `mechanism`, `summary`,
  `rationale`, `riskCaveat`, `moodSummary`. Do not pattern-match on them; use
  semantic search or LLM interpretation for downstream classification.
- **Token counts are LLM telemetry**, useful for cost tracking and not for
  interpreting the analytical content.
- **Period values are inclusive-start, exclusive-end.** `periodStart` of
  `2026-04-20` and `periodEnd` of `2026-04-27` covers Apr 20 through Apr 26
  inclusive — Apr 27 is **not** in the period.
