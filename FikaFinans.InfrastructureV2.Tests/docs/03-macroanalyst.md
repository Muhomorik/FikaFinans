# Agent 03: MacroAnalyst

> Read the three weekly macro analytics JSON reports and emit a structured macro context with regime, catalysts, and rotation themes.

## Execution type

🤖 LLM

## Inputs

| Source | What for |
| --- | --- |
| `inputs/analytics-weekly-summary.json` | `themes[]` by `confidence`; primary input for `macro_regime` and `regime_confidence` |
| `inputs/analytics-substitution-chain.json` | `chains[]` (capitalFleeing → flowsToward); primary input for `rotation_themes` and `catalysts` |
| `inputs/analytics-rotation-targets.json` | `targets[]` graded by `signalStrength`; secondary signal mapped to `rotation_themes.signal_strength` |
| `01-dataloader-{iso_week}-{run_id}.json` | Fund category list — used to constrain catalyst and theme matching to real categories |

Schema reference: `inputs/analytics-json-schema.md`.

### Input expectations

- All three JSON files share the same `periodIsoWeek` value, and the FK chain holds: `analytics-substitution-chain.weeklySummaryRunId == analytics-weekly-summary.runId`, and `analytics-rotation-targets.substitutionChainRunId == analytics-substitution-chain.runId`.
- The CSV `{iso_week}` (from DataLoader) must equal the JSON `periodIsoWeek` — the orchestrator already enforces this triple-match before this agent runs, but defensive recheck is cheap.
- Any input with `status == "Failed"` halts the chain — content of a failed run is unreliable.
- Any input with `status == "Partial"` is consumable but a warning is logged.
- DataLoader output provides the universe of fund categories. The agent must not invent categories outside this list.

## Outputs

### Output file

Pattern: `03-macro-{iso_week}-{run_id}.json`

### Output schema

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "source_run_ids": {
    "weekly_summary_run_id": "GUID",
    "substitution_chain_run_id": "GUID",
    "rotation_targets_run_id": "GUID"
  },
  "macro_regime": "RiskOn" | "RiskOff" | "Mixed" | "Stagflation" | "Crisis",
  "macro_regime_secondary": "string (optional qualifier, e.g. 'mixed risk-off')",
  "regime_confidence": 0.0,
  "net_mood_input": "RiskOn" | "RiskOff" | "Mixed",
  "catalysts": [
    {
      "name": "string (e.g. 'Hormuz disruption')",
      "intensity": "low" | "medium" | "high",
      "weeks_active": int,
      "affected_categories": ["category strings — must exist in fund universe"],
      "rationale": "string (≤2 sentences)"
    }
  ],
  "rotation_themes": [
    {
      "id": "rot_theme_{slug}_{iso_week}",
      "label": "string",
      "signal_strength": "Strong" | "Moderate" | "Weak",
      "affected_categories": ["category strings — must exist in fund universe"],
      "rationale": "string (≤2 sentences)",
      "source_chain": {
        "capital_fleeing": "string (verbatim from input)",
        "flows_toward": "string (verbatim from input)"
      } | null
    }
  ]
}
```

`net_mood_input` is the `netMood` value from `analytics-weekly-summary.json` — preserved verbatim so downstream agents can see the upstream sentiment label even when it differs from the derived `macro_regime`.

`source_run_ids` lets downstream agents and audit tools trace any output back to the exact upstream runs.

The `rotation_themes[].source_chain` field carries the chain that justifies the theme (when the theme is grounded in a single chain). Null when the theme is a synthesis of multiple chains.

## Configuration consumed

None for v1. Future versions may consume rotation-strength thresholds.

## Vocabulary owned

| Enum | Values | Notes |
| --- | --- | --- |
| `macro_regime` | `RiskOn`, `RiskOff`, `Mixed`, `Stagflation`, `Crisis` | Superset of upstream `MarketSentiment` — adds `Stagflation` and `Crisis` for tail-risk regimes the upstream `netMood` enum cannot express. Names are PascalCase to align with upstream camelCase enum style. |
| `intensity` | `low`, `medium`, `high` | Agent-derived; not an upstream enum |
| `signal_strength` | `Strong`, `Moderate`, `Weak` | Mirrors upstream `SignalStrength` exactly. `Weak` retains the contrarian semantics from `OpportunityScanRun` |

When mapping upstream `netMood` to `macro_regime`:

| Upstream `netMood` | Default `macro_regime` mapping | Override conditions |
| --- | --- | --- |
| `RiskOn` | `RiskOn` | Promote to `Stagflation` if substitution chains repeatedly cite oil/inflation as `mechanism` |
| `RiskOff` | `RiskOff` | Promote to `Crisis` if ≥2 chains cite contagion / liquidity / panic |
| `Mixed` | `Mixed` | Promote to `Stagflation` if inflation+oil dominates the mechanisms; `Crisis` if recession/contagion dominates |

## What it does

1. Verify the three JSON files all carry the same `periodIsoWeek` and that the FK chain holds (`weeklySummaryRunId`, `substitutionChainRunId`). Halt if not.
2. Verify all three have `status ∈ {"Success", "Partial"}` — halt on `"Failed"`, warn on `"Partial"`.
3. Verify the JSON `periodIsoWeek` matches the DataLoader output's `iso_week`. Halt on mismatch.
4. Extract the unique set of `metadata.category` values from the DataLoader output.
5. Send the three JSON payloads (their full bodies — `themes[]`, `chains[]`, `targets[]`, plus `netMood` and `moodSummary`) plus the category list to the LLM with the system prompt below.
6. Validate the LLM response: `macro_regime` ∈ enum, every catalyst has at least one valid `affected_categories` entry, every rotation theme has at least one valid `affected_categories` entry.
7. Drop any catalyst or theme whose category list becomes empty after filtering against the universe; log warnings.
8. If the response is invalid JSON or fails enum validation, retry once with a corrective prompt. Halt after second failure.
9. Emit the structured context with `source_run_ids` populated from the three input files' `runId` fields.

## LLM prompt skeleton

### System prompt

```text
You are a macro regime extraction agent. You receive three weekly market analytics
JSON reports produced upstream by the pipeline, and a list of fund
categories that exist in the user's portfolio universe.

The three inputs are:
- WeeklySummaryRun (analytics-weekly-summary.json): a netMood label, a moodSummary
  paragraph, and a themes[] array. Each theme has {category, summary, confidence,
  sentiment}. confidence ∈ {High, Medium, Low}; sentiment ∈ {RiskOn, RiskOff, Mixed}.
- SubstitutionChainRun (analytics-substitution-chain.json): a chains[] array of
  {capitalFleeing, flowsToward, mechanism} — capital is fleeing X, flowing toward Y,
  because of mechanism Z (which references a theme name from the weekly summary).
- OpportunityScanRun (analytics-rotation-targets.json): a targets[] array of
  {category, signalStrength, rationale, riskCaveat}. signalStrength ∈ {Strong,
  Moderate, Weak}, where Weak is a CONTRARIAN play (target is on the fleeing side
  of a chain — bet on reversal).

The free-form `category` strings in these inputs are NOT drawn from the user's fund
universe — they are prose. You must MAP them to the user's fund categories.

Your job is to extract:
1. A single macro_regime label from {RiskOn, RiskOff, Mixed, Stagflation, Crisis}.
   The base regime tracks the upstream netMood, but you may promote to Stagflation
   (oil/inflation dominant in chain mechanisms) or Crisis (contagion/liquidity/panic
   dominant) when the underlying chains warrant it.
2. Active catalysts — geopolitical or macro events affecting specific fund categories.
3. Rotation themes — capital-flow patterns the chains identify, with signal_strength
   from {Strong, Moderate, Weak}.

Constraints:
- macro_regime MUST be one of the five enum values.
- Every catalyst must have at least one affected_categories entry from the provided
  fund category list. If you cannot match, drop the catalyst.
- Every rotation theme must have at least one affected_categories entry from the list.
- When a rotation theme is grounded in exactly one chain, populate source_chain with
  the verbatim capitalFleeing/flowsToward strings. Otherwise set source_chain to null.
- Keep all rationale strings to two sentences or fewer.
- regime_confidence is 0..1; higher when the three reports converge, lower when they
  conflict (e.g. weekly summary says RiskOn but chains scream Crisis).
- Return valid JSON conforming to the schema in the user message.

Do NOT invent regimes, catalysts, or themes that are not supported by the reports.
Do NOT match a catalyst to a category unless the chain's mechanism or a theme's
summary directly implies that category is affected.
```

### User prompt template

```text
ISO week (periodIsoWeek): {iso_week}
Upstream run IDs:
- weekly summary runId: {weekly_summary_run_id}
- substitution chain runId: {substitution_chain_run_id}
- rotation targets runId: {rotation_targets_run_id}

Fund categories in universe (use ONLY these for matching):
{category_list}

=== analytics-weekly-summary.json ===
{weekly_summary_json}

=== analytics-substitution-chain.json ===
{substitution_chain_json}

=== analytics-rotation-targets.json ===
{rotation_targets_json}

Return JSON exactly conforming to this schema:
{schema_excerpt}
```

### Retry corrective prompt

```text
Your previous response failed validation: {error_message}

Re-emit the response with the validation error fixed. Stay within the enum values
and use only categories from the provided list. Return valid JSON only — no prose
before or after.
```

## Failure modes

| Trigger | Behavior |
| --- | --- |
| The three JSON files have different `periodIsoWeek` values | Halt — `03-error-{iso_week}-{run_id}.json` |
| FK chain broken (`weeklySummaryRunId` or `substitutionChainRunId` mismatch) | Halt — inputs are not from the same chained run |
| Any input has `status == "Failed"` | Halt — content of a failed run is unreliable |
| Any input has `status == "Partial"` | Continue with warning |
| JSON `periodIsoWeek` does not match DataLoader's `iso_week` | Halt — bundle drift |
| `themes[]`, `chains[]`, or `targets[]` empty on a `Success` run | Valid input — treat as a structurally quiet week; the agent can still emit `macro_regime` |
| LLM returns malformed JSON | Retry once with corrective prompt; halt after second failure |
| `macro_regime` not in enum | Treat as schema violation; retry once |
| A catalyst has no valid `affected_categories` after filtering | Drop the catalyst; warn |
| A rotation theme has no valid `affected_categories` after filtering | Drop the theme; warn |
| All catalysts and all themes drop out | Valid output (regime alone is enough); warn that universe-coupling is weak this week |
| `regime_confidence` outside [0, 1] | Clamp; warn |

## Test fixtures

| Scenario | Inputs | Expected |
| --- | --- | --- |
| Stagflation week (e.g. 2026-W14) | The three real JSON files for that week, with chain mechanisms citing oil/inflation | `macro_regime = Stagflation`, ≥1 energy catalyst with `affected_categories` including "Branschfond, Energi" or similar, ≥1 rotation theme |
| Risk-on calm week | Synthetic JSON: `netMood = RiskOn`, `themes[]` mild and `chains[]` empty | `macro_regime = RiskOn`, `catalysts = []`, 1–2 rotation themes with `signal_strength = Weak` or `Moderate` |
| Conflicting reports | Summary `netMood = RiskOn` but chains scream contagion | `regime_confidence ≤ 0.5`, agent emits `Crisis` or `Mixed`; rationale notes the disagreement |
| LLM hallucinates a non-existent category | Output references "AI Hardware" not in universe | Filtered out, warning logged, theme dropped if empty |
| LLM returns invalid JSON | Mock response with trailing comma | Retry succeeds on second pass |
| Failed input | `analytics-substitution-chain.json` has `status = "Failed"` | Halt with explicit error file |
| FK mismatch | substitution-chain's `weeklySummaryRunId` doesn't match weekly summary's `runId` | Halt — bundle drift |
| Period mismatch | Three JSON files have `periodIsoWeek = 2026-W17` but DataLoader output is `2026-W18` | Halt with explicit error file |
| Empty themes + chains + targets | All three Success but arrays empty (a structurally quiet week) | Valid output; `macro_regime = Mixed`, `regime_confidence ≤ 0.4`, `catalysts = []`, `rotation_themes = []` |

## Evaluation prompt — AI Foundry custom rubric

```text
You are evaluating the output of a macro regime extraction agent.

Inputs you will see:
- Three weekly market reports (markdown)
- The fund category list that was provided to the agent
- The agent's structured JSON output

Score the output on these five dimensions, 1–5 each:

1. Regime grounding (1-5)
   Is the chosen macro_regime supported by the reports' explicit narrative?
   - 5: Regime is the natural reading of the reports.
   - 3: Defensible but reports could support a different label.
   - 1: Regime contradicts the reports.

2. Catalyst accuracy (1-5)
   Are the catalysts actually mentioned in the reports? Are their
   affected_categories plausible matches against real fund categories?
   - 5: All catalysts are explicitly named in reports; categories match tightly.
   - 3: Catalysts are inferred from secondary mentions; categories are loose.
   - 1: Hallucinated catalysts or category mismatches.

3. Theme fidelity (1-5)
   Do the rotation_themes correspond to flows the substitution-chain or
   rotation-targets reports describe?
   - 5: Each theme is directly traceable to a Fleeing/Toward block or rotation target.
   - 3: Themes are paraphrased loosely.
   - 1: Themes are inferred but not stated in reports.

4. Schema compliance (1-5)
   - 5: Valid JSON, all enums respected, all required fields present.
   - 1: Invalid JSON or enum violation.

5. Confidence calibration (1-5)
   Is regime_confidence appropriately scaled?
   - 5: A confident single-narrative report gets >0.7; conflicting reports get <0.5.
   - 1: Confidence value contradicts narrative agreement.

For each dimension output:
- Score (1-5)
- One-sentence justification with a specific quote or reference

Then compute the overall score as the mean across the five dimensions.
Flag the case for human review if any dimension scores ≤ 2.
Flag for celebration if the overall mean is ≥ 4.5.
```

## Edge cases

- Reports with no clear regime signal → emit `Mixed` with `regime_confidence ≤ 0.5`.
- Catalyst spans multiple themes (e.g. Hormuz affects energy AND inflation) → either is valid; pick the more directly affected.
- A rotation theme's `signal_strength` mapping differs across inputs (e.g. an `OpportunityScanRun` target is `Weak` but the underlying chain is the only one in `chains[]`, which would normally read as `Moderate`) → use the upstream `signalStrength` from `targets[]` when a target maps directly to a theme; otherwise infer from chain count (≥2 chains agreeing → `Strong`, 1 chain → `Moderate`).
- A "fading" catalyst the reports flag as cooling (chain mechanism mentions "easing") → emit with `intensity = low` rather than dropping.
- A `Weak` (contrarian) target → keep `signal_strength = Weak` and note in `rationale` that this is a reversal bet, not a momentum trade.
- The free-form `category` strings in upstream JSON are prose — `"AI and semiconductor leadership"`, `"Energy"`, `"Healthcare innovation"` — not fund categories. The LLM must do the prose→universe mapping, not pattern-match.
- Reports written in Swedish or English alike — the LLM should handle both, but `category` matching against the fund universe is exact-string.
- `weeks_active` is an estimate based on chain `mechanism` text (e.g. "tensions for the past 8 weeks"); when not stated, default to `1` (the current week only).
- Token cost: the JSON inputs are small (~1–3 KB each) compared to the previous markdown reports — total prompt size with 50 fund categories and full payloads is typically under 8K tokens.
