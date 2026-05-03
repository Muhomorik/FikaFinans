# Agent 07: ThesisValidator

> Determine whether each fund's investment thesis is Valid, Partial, Invalid, or NotApplicable based on signal + macro alignment + catalyst combination.

## Execution type
🔀 Hybrid — code computes a baseline from a decision matrix; LLM refines edge cases that produce conflicting inputs.

## Inputs

| Source | What for |
|---|---|
| `06-catalyst-{iso_week}-{run_id}.json` | Per-fund records with signal, macro_alignment, and catalyst |

## Outputs

### Output file
Pattern: `07-thesis-{iso_week}-{run_id}.json`

### Output schema

Adds `thesis_validity` and `thesis_rationale` to each fund record. All prior fields preserved.

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
      "catalyst": { /* preserved, may be null */ },

      "thesis_validity": "Valid" | "Partial" | "Invalid" | "NotApplicable",
      "thesis_rationale": "string (≤2 sentences)",
      "thesis_method": "matrix" | "llm_refinement"
    }
  ]
}
```

## Configuration consumed
None for v1. Future versions may consume the decision matrix as a tunable mapping.

## Vocabulary owned

| `thesis_validity` | Meaning |
|---|---|
| `Valid` | Story intact — signal and supporting context align |
| `Partial` | Some support but not all conditions met |
| `Invalid` | Thesis broken — price action contradicts the fundamental narrative |
| `NotApplicable` | No directional signal to anchor a thesis to (Neutral funds) |

## Decision matrix (code baseline)

The matrix produces a thesis_validity from the four-tuple `(signal, catalyst_present, macro_alignment, currently_held)`. Most cases are deterministic; a small slice invokes the LLM for refinement.

| Signal | Catalyst? | Macro alignment | → Baseline thesis | LLM refinement? |
|---|---|---|---|---|
| Strength | yes | Strong | **Valid** | no |
| Strength | yes | Partial / None | **Partial** | optional (catalyst without macro tailwind) |
| Strength | no | Strong | **Valid** | no |
| Strength | no | Partial | **Partial** | no |
| Strength | no | None | **Partial** | no |
| Weakness | yes | Strong | **Invalid** | yes — confirm contradiction |
| Weakness | yes | Partial / None | **Invalid** | yes |
| Weakness | no | Strong | **Partial** | yes — momentum decay vs macro support |
| Weakness | no | Partial / None | **Partial** | no |
| Forming | yes | Strong | **Partial** | no |
| Forming | no | Strong | **Partial** | no |
| Forming | yes/no | None | **NotApplicable** | no |
| Neutral | any | any | **NotApplicable** | no |

The "thesis = Invalid" case (Weakness + Catalyst still active) is the most important pattern: **the catalyst is still firing externally, but the fund's price action is breaking down**. That's exactly when to exit — the thesis is broken regardless of what the macro reports say.

## Worked examples

### Global Energy (LU0256331488) — Invalid (the canonical exit case)

| Field | Value |
|---|---|
| `signal` | Weakness |
| `catalyst` | Hormuz disruption, Direct, intensity high |
| `macro_alignment` | Strong (energy theme still active) |
| Baseline | Invalid |
| LLM refinement | Confirmed Invalid — momentum reversed despite catalyst still firing |
| `thesis_validity` | `Invalid` |
| `thesis_rationale` | "Catalyst still active but price action reversed — Hormuz tailwind no longer translating to NAV gains. Thesis broken regardless of external narrative." |

### Em Mkts (LU0106252389) — Partial

| Field | Value |
|---|---|
| `signal` | Strength |
| `catalyst` | null |
| `macro_alignment` | Partial |
| Baseline | Partial |
| LLM refinement | no — clean matrix case |
| `thesis_validity` | `Partial` |
| `thesis_rationale` | "Strong technicals (3/3 windows positive, 12w Sharpe +1.82) but no specific catalyst or strong macro tailwind anchoring the entry." |

### Frontier Mkts (LU0562313402) — NotApplicable (Neutral)

| Field | Value |
|---|---|
| `signal` | Neutral |
| `catalyst` | null |
| `macro_alignment` | None |
| Baseline | NotApplicable |
| `thesis_validity` | `NotApplicable` |
| `thesis_rationale` | "No directional signal — no thesis to validate." |

## When the LLM is invoked

The LLM is consulted only for the rows in the matrix marked "yes" or "optional" — roughly when the inputs are conflicting (Weakness + active catalyst, or Strength with catalyst but weak macro). The LLM's job is to write a more specific `thesis_rationale` and confirm the baseline label, not to override it. If the LLM returns a label that differs from the baseline by more than one step (e.g. Valid → Invalid), the agent logs a warning and keeps the baseline.

## LLM prompt skeleton

### System prompt
```
You are a thesis validator. You will be given a fund's signal, catalyst (if any),
macro alignment, and a baseline thesis_validity from a decision matrix.

Your job is to either CONFIRM the baseline or recommend an ADJACENT label
(Valid ↔ Partial, Partial ↔ Invalid). You may not jump two steps.

Constraints:
- Your output MUST be valid JSON: {"thesis_validity": "<label>", "rationale": "..."}.
- thesis_validity must be one of {Valid, Partial, Invalid, NotApplicable}.
- rationale must be ≤2 sentences and specific to the inputs (cite the catalyst,
  signal, or macro alignment by name).
- If the baseline is correct, restate it; do not invent a different one to seem useful.

The most consequential pattern: signal=Weakness with active catalyst means the
thesis is BROKEN even though the catalyst is firing. Price action overrides
narrative.
```

### User prompt template
```
Fund: {fund.metadata.name} ({fund.isin})
Category: {fund.metadata.category}

Signal: {fund.signal} ({fund.rule_fired})
Catalyst: {fund.catalyst or "none"}
Macro alignment: {fund.macro_alignment}
Currently held: {fund.currently_held}

Decision-matrix baseline thesis_validity: {baseline}

Confirm or adjust by one step. Return JSON with rationale.
```

## Failure modes

| Trigger | Behavior |
|---|---|
| LLM returns invalid JSON | Retry once with corrective prompt; on second failure use baseline |
| LLM returns label two steps from baseline (e.g. Valid → Invalid) | Override LLM, use baseline, log warning |
| LLM returns label not in enum | Use baseline; log warning |
| Fund has `signal = null` (DataLoader / SignalScorer skipped it) | `thesis_validity = NotApplicable`, no LLM call |
| Catalyst object exists but exposure_type missing | Treat as no catalyst for matrix purposes; warn |

## Test fixtures

| Scenario | Inputs | Expected | LLM call? |
|---|---|---|---|
| Strength + catalyst + Strong macro | Energy fund pre-rollover | Valid | no |
| Strength only (no catalyst, no macro) | Smlr Coms momentum | Partial | no |
| Weakness + catalyst Direct | Energy post-rollover | Invalid (matrix), confirmed by LLM | yes |
| Weakness + no catalyst, with macro Strong | Theoretical: defensive theme weakening | Partial (matrix), LLM confirms | yes |
| Weakness + no catalyst, no macro | Indian Opports clean sell | Partial | no |
| Forming + Strong macro | Promoted near-buy | Partial | no |
| Neutral (any combination) | Most universe in normal weeks | NotApplicable | no |
| LLM tries to jump Valid → Invalid | Override scenario | Use baseline (Valid), warn | yes |

## Evaluation prompt — AI Foundry custom rubric

```
You are evaluating ThesisValidator's output for a single fund.

Inputs you will see:
- The fund's signal, catalyst (or null), macro_alignment, currently_held
- The agent's verdict: thesis_validity + thesis_rationale + thesis_method

Score on four dimensions, 1-5 each:

1. Matrix consistency (1-5)
   Does the assigned thesis_validity match the decision matrix for this input combo?
   - 5: Exact match.
   - 3: Adjacent (Valid↔Partial or Partial↔Invalid) — defensible LLM refinement.
   - 1: Two steps off or wrong direction.

2. The "Invalid catalyst-still-active" detection (1-5, only when applicable)
   When signal=Weakness and catalyst is Direct/active, did the agent correctly
   emit Invalid?
   - 5: Yes, with a rationale that explicitly notes the price-vs-narrative split.
   - 3: Emitted Partial when Invalid was warranted.
   - 1: Emitted Valid (catastrophic — would prevent the exit).

3. Rationale quality (1-5)
   - 5: ≤2 sentences, names the catalyst/macro/signal specifically, explains the link.
   - 3: Vague but plausible.
   - 1: Generic, off-topic, or contradicts the verdict.

4. Method appropriateness (1-5)
   - 5: matrix used for clean cases; llm_refinement only for genuinely conflicting inputs.
   - 1: LLM consulted unnecessarily, or matrix used when refinement was needed.

For each dimension output:
- Score (1-5)
- One-sentence justification

Flag for review if dimension 2 ≤ 2 (this is the rotation-trigger case).
```

## Edge cases

- A fund with `catalyst.exposure_type = "Indirect"` and `signal = Weakness`: treat as if `catalyst is present` for matrix purposes (still triggers Invalid path), but LLM's rationale should note the indirect link is also breaking.
- `signal = Forming` with `macro_alignment = None`: cannot occur in v1 because MacroAligner only promotes Neutral → Forming when macro is Strong. Defensive: treat as NotApplicable.
- A fund with conflicting inputs (e.g. Strength + catalyst that the substitution-chain reports as fading): LLM may pick Partial — that's acceptable. The matrix's Valid is the upper bound.
- Funds with `currently_held = false` get the same thesis treatment as held funds. The held flag is a portfolio-state concern that PortfolioConstructor handles, not a thesis-validity concern.
