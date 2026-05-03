# Agent 03: MacroAnalyst

> Read the three weekly macro markdown reports and emit a structured macro context with regime, catalysts, and rotation themes.

## Execution type
🤖 LLM

## Inputs

| Source | What for |
|---|---|
| `analytics-weekly-summary.md` | Themes by confidence; primary input for `macro_regime` |
| `analytics-substitution-chain.md` | Capital flow narratives; primary input for `rotation_themes` and `catalysts` |
| `analytics-rotation-targets.md` | High-conviction theme bets; secondary signal for `signal_strength` |
| `01-dataloader-{iso_week}-{run_id}.json` | Fund category list — used to constrain catalyst and theme matching to real categories |

### Input expectations
- All three markdown files share the same `iso_week` (per their YAML front-matter).
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
  "macro_regime": "Risk-on" | "Mixed" | "Stagflation" | "Crisis",
  "macro_regime_secondary": "string (optional qualifier, e.g. 'mixed risk-off')",
  "regime_confidence": 0.0,
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
      "matches_categories": ["category strings — must exist in fund universe"],
      "rationale": "string (≤2 sentences)"
    }
  ]
}
```

## Configuration consumed
None for v1. Future versions may consume rotation-strength thresholds.

## Vocabulary owned

| Enum | Values |
|---|---|
| `macro_regime` | `Risk-on`, `Mixed`, `Stagflation`, `Crisis` |
| `intensity` | `low`, `medium`, `high` |
| `signal_strength` | `Strong`, `Moderate`, `Weak` |

## What it does

1. Verify the three markdown files all carry the same `iso_week` in their YAML front-matter. Halt if not.
2. Extract the unique set of `metadata.category` values from the DataLoader output.
3. Send the three files plus the category list to the LLM with the system prompt below.
4. Validate the LLM response: `macro_regime` ∈ enum, every catalyst has at least one valid `affected_categories` entry, every theme has at least one valid `matches_categories` entry.
5. Drop any catalyst or theme whose category list becomes empty after filtering against the universe; log warnings.
6. If the response is invalid JSON or fails enum validation, retry once with a corrective prompt. Halt after second failure.
7. Emit the structured context.

## LLM prompt skeleton

### System prompt
```
You are a macro regime extraction agent. You receive three weekly market reports
(weekly summary, substitution chain, rotation targets) and a list of fund categories
that exist in the user's portfolio universe.

Your job is to extract:
1. A single macro_regime label from {Risk-on, Mixed, Stagflation, Crisis}.
2. Active catalysts — geopolitical or macro events affecting specific fund categories.
3. Rotation themes — capital-flow patterns the reports identify, with signal strength
   from {Strong, Moderate, Weak}.

Constraints:
- macro_regime MUST be one of the four enum values.
- Every catalyst must have at least one affected_category from the provided fund
  category list. If you cannot match, drop the catalyst.
- Every rotation theme must have at least one matched_category from the list.
- Keep all rationale strings to two sentences or fewer.
- regime_confidence is 0..1; higher when reports converge, lower when they conflict.
- Return valid JSON conforming to the schema in the user message.

Do NOT invent regimes, catalysts, or themes that are not supported by the reports.
Do NOT match a catalyst to a category unless the report's narrative directly implies
that category is affected.
```

### User prompt template
```
ISO week: {iso_week}

Fund categories in universe (use ONLY these for matching):
{category_list}

=== analytics-weekly-summary.md ===
{file_1_content}

=== analytics-substitution-chain.md ===
{file_2_content}

=== analytics-rotation-targets.md ===
{file_3_content}

Return JSON exactly conforming to this schema:
{schema_excerpt}
```

### Retry corrective prompt
```
Your previous response failed validation: {error_message}

Re-emit the response with the validation error fixed. Stay within the enum values
and use only categories from the provided list. Return valid JSON only — no prose
before or after.
```

## Failure modes

| Trigger | Behavior |
|---|---|
| The three markdown files have different `iso_week` values | Halt — `03-error-{iso_week}-{run_id}.json` |
| LLM returns malformed JSON | Retry once with corrective prompt; halt after second failure |
| `macro_regime` not in enum | Treat as schema violation; retry once |
| A catalyst has no valid `affected_categories` after filtering | Drop the catalyst; warn |
| A rotation theme has no valid `matches_categories` after filtering | Drop the theme; warn |
| All catalysts and all themes drop out | Valid output (regime alone is enough); warn that universe-coupling is weak this week |
| `regime_confidence` outside [0, 1] | Clamp; warn |

## Test fixtures

| Scenario | Inputs | Expected |
|---|---|---|
| Stagflation week (e.g. 2026-W14) | The three real markdown files for that week | `macro_regime = Stagflation`, ≥1 energy catalyst with `affected_categories` including "Branschfond, Energi" or similar, ≥1 rotation theme |
| Risk-on calm week | Synthetic markdown with no catalysts and bland themes | `macro_regime = Risk-on`, `catalysts = []`, 1–2 rotation themes with `signal_strength = Weak` or `Moderate` |
| Conflicting reports | Summary says Risk-on, rotation says Crisis | `regime_confidence ≤ 0.5`, agent picks one; rationale notes the disagreement |
| LLM hallucinates a non-existent category | Output references "AI Hardware" not in universe | Filtered out, warning logged, theme dropped if empty |
| LLM returns invalid JSON | Mock response with trailing comma | Retry succeeds on second pass |

## Evaluation prompt — AI Foundry custom rubric

```
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
- A rotation theme's `signal_strength` differs between substitution-chain and rotation-targets → use the stronger of the two and mention the discrepancy in `rationale`.
- A "fading" catalyst the reports flag as cooling → emit with `intensity = low` rather than dropping.
- Reports written in Swedish or English alike — the LLM should handle both, but `category` matching is exact-string against the universe.
- `weeks_active` is an estimate based on report context (e.g. "tensions for the past 8 weeks"); when not stated, default to `1` (the current week only).
