# Agent 06: CatalystTagger

> Tag each fund with the active macro catalyst that affects it (or null if none).

## Execution type
🤖 LLM

## Inputs

| Source | What for |
|---|---|
| `05-macro-align-{iso_week}-{run_id}.json` | Per-fund records with metadata, signals, and macro alignment |
| `03-macro-{iso_week}-{run_id}.json` | Active catalysts list from MacroAnalyst |

## Outputs

### Output file
Pattern: `06-catalyst-{iso_week}-{run_id}.json`

### Output schema

Adds a `catalyst` object (or null) to each fund record. All prior fields preserved.

```json
{
  "generated_at": "...",
  "iso_week": "...",
  "config_version": "1.0.0",
  "funds": [
    {
      "isin": "...",
      "metadata": { /* preserved */ },
      "metrics": { /* preserved */ },
      "signal": "...",
      "macro_alignment": "...",
      "matched_theme": { /* preserved */ },

      "catalyst": {
        "name": "string (e.g. 'Hormuz disruption')",
        "intensity": "low" | "medium" | "high",
        "weeks_active": int,
        "exposure_type": "Direct" | "Indirect",
        "rationale": "string (≤2 sentences)"
      } | null
    }
  ]
}
```

## Configuration consumed
None for v1.

## Vocabulary owned

| `exposure_type` | Meaning |
|---|---|
| `Direct` | The fund's category is the catalyst's primary affected category (e.g. Energy fund + Hormuz catalyst) |
| `Indirect` | The fund is affected via correlation or sector adjacency, not as the primary beneficiary |

## What it does

1. Pull the list of active catalysts from `03-macro` output. Each has `name`, `intensity`, `weeks_active`, `affected_categories`.
2. For each fund, determine if any catalyst applies:
   - **Direct match (LLM):** Send the fund's `metadata.category` and `metadata.name` along with each catalyst's `affected_categories` and ask the LLM to classify exposure as Direct, Indirect, or None.
   - **No match:** `catalyst = null`.
3. If multiple catalysts apply (rare), keep only the one with the strongest direct exposure. If still tied, pick the catalyst with higher `intensity`.
4. Preserve `intensity` and `weeks_active` from the source catalyst; do not modify.

The agent does **not** invent new catalysts — only assigns existing ones. If no catalyst's `affected_categories` matches a fund (even loosely), `catalyst = null`.

## Worked examples

### Global Energy (LU0256331488) — Direct catalyst

| Field | Value |
|---|---|
| `metadata.category` | "Branschfond, Energi" |
| Active catalyst | "Hormuz disruption", intensity=high, affected_categories=["Branschfond, Energi", "Energy", ...] |
| LLM verdict | `Direct` exposure |
| `catalyst.name` | "Hormuz disruption" |
| `catalyst.intensity` | "high" |
| `catalyst.weeks_active` | 8 |
| `catalyst.exposure_type` | "Direct" |
| `catalyst.rationale` | "Energy sector fund directly benefits from oil price spikes from Hormuz tensions." |

### Glbl Alt Engy (LU1983299162) — Indirect catalyst

| Field | Value |
|---|---|
| `metadata.category` | "Branschfond, Ny energi" |
| Active catalyst | "Hormuz disruption" affecting Energy categories |
| LLM verdict | `Indirect` exposure (alt energy benefits secondarily as a hedge against fossil fuel volatility) |
| `catalyst.exposure_type` | "Indirect" |
| `catalyst.rationale` | "Alternative energy fund benefits indirectly as investors hedge fossil fuel exposure during oil shocks." |

### Em Mkts (LU0106252389) — No catalyst

| Field | Value |
|---|---|
| `metadata.category` | "Tillväxtmarknader" |
| Active catalysts | "Hormuz disruption" (Energy), "AI capex cycle" (Tech) |
| LLM verdict | None (Em Mkts is not a primary or secondary beneficiary of either) |
| `catalyst` | `null` |

## LLM prompt skeleton

### System prompt
```
You are a catalyst tagging agent. You receive a fund (name + category) and a list
of active macro catalysts (each with affected_categories). Your job is to classify
the fund's exposure to each catalyst as one of:

- "Direct": the fund's category is in the catalyst's affected_categories list,
  or the fund's primary investment thesis is the catalyst.
- "Indirect": the fund benefits secondarily through correlation, sector adjacency,
  or hedge-like behavior. NOT the primary beneficiary.
- "None": no meaningful exposure.

Return JSON for each catalyst evaluated. If none apply, return an empty list.

Constraints:
- Do not invent catalysts. Only consider those provided in the input.
- Direct exposure REQUIRES the fund category being in or very close to the catalyst's
  affected_categories. Cousin sectors are Indirect at best.
- A fund can have at most one catalyst tagged in the final output. If multiple
  apply, the upstream agent will pick the strongest.
- Each rationale must be ≤2 sentences and reference a specific mechanism.
```

### User prompt template
```
Fund:
- name: {fund.metadata.name}
- category: {fund.metadata.category}

Active catalysts:
{catalyst_list_json}

Classify the fund's exposure to each catalyst. Return JSON:
[
  {
    "catalyst_name": "...",
    "exposure_type": "Direct" | "Indirect" | "None",
    "rationale": "..."
  }
]
```

### Retry corrective prompt
```
Your previous response failed validation: {error_message}

Re-emit valid JSON. Use only the catalyst names from the input. Stay within the
exposure_type enum {Direct, Indirect, None}.
```

## Failure modes

| Trigger | Behavior |
|---|---|
| `03-macro` output has `catalysts = []` | All funds get `catalyst = null`; no LLM calls |
| LLM returns invalid JSON | Retry once with corrective prompt; on second failure, set `catalyst = null` for that fund and warn |
| LLM references a catalyst name not in the active list | Drop that entry; warn |
| Multiple Direct exposures classified for one fund | Pick the highest-intensity catalyst; tie-break by longest `weeks_active` |
| Fund category is null | `catalyst = null` immediately; no LLM call |

## Test fixtures

| Scenario | Inputs | Expected |
|---|---|---|
| Direct match | Energy fund + Hormuz catalyst | `catalyst.exposure_type = "Direct"`, full object populated |
| Indirect match | Alt energy fund + Hormuz catalyst | `catalyst.exposure_type = "Indirect"` |
| No match | Em Mkts fund + Hormuz only | `catalyst = null` |
| Two competing catalysts | Tech fund + AI catalyst + Hormuz catalyst | Pick AI (higher Direct match), drop Hormuz |
| LLM hallucinates a catalyst | Output references "Banking crisis" not in input | Filtered out; warn |
| Empty catalysts in macro context | `03-macro` had `catalysts = []` | All funds catalyst=null, no LLM cost |

## Evaluation prompt — AI Foundry custom rubric

```
You are evaluating CatalystTagger's output for a single fund.

Inputs you will see:
- The fund's metadata (name, category)
- The active catalysts from MacroAnalyst (each with affected_categories)
- The agent's verdict (catalyst object or null)

Score on four dimensions, 1-5 each:

1. Match correctness (1-5)
   - 5: Catalyst is appropriate; the fund clearly fits affected_categories.
   - 3: Plausible but loose; could defensibly be null.
   - 1: Wrong catalyst or null when there's an obvious match.

2. Exposure type calibration (1-5)
   - 5: Direct used only when the category is in affected_categories or near-equivalent;
        Indirect used for genuine secondary beneficiaries.
   - 3: Borderline call (Direct vs Indirect).
   - 1: Direct used loosely; Indirect used when None applies.

3. Rationale quality (1-5)
   - 5: Specific mechanism is explained in ≤2 sentences with a concrete economic link.
   - 3: Vague but plausible.
   - 1: Generic boilerplate or unclear reasoning.

4. Conservatism (1-5)
   - 5: When the catalyst doesn't really fit, the agent returns null instead of forcing a tag.
   - 1: Tags catalysts even when affected_categories clearly excludes the fund.

For each dimension output:
- Score (1-5)
- One-sentence justification, citing the fund category and catalyst's affected_categories specifically.

Flag for review if Match correctness ≤ 2 (this is the most consequential dimension).
```

## Edge cases

- A catalyst with `affected_categories = []` (empty) — should never happen post-MacroAnalyst validation, but defensively skip such catalysts entirely.
- Funds with `signal = Neutral` and no active catalysts: `catalyst = null` is the dominant outcome (~50–80% of universe in normal weeks).
- Catalyst tagged on a fund with `signal = Weakness`: this is the rotation-trigger pattern. Energy with Direct Hormuz catalyst + Weakness signal → ThesisValidator decides Invalid (price action contradicts catalyst → exit).
- Reports may flag a "fading" catalyst with `intensity = low`. These should still be tagged on directly-affected funds; ThesisValidator and Recommender handle the de-emphasis.
