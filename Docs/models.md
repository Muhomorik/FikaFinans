# Foundry models

The Foundry models deployed for FikaFinans, plus the candidates we rejected and why.

## Instructions for AI Agents

**CRITICAL:** When editing this document, AI assistants MUST verify before saving:

- [ ] No real Azure resource names — use `<your-...>` placeholders
- [ ] No real endpoint URLs — use `<your-...>` placeholders
- [ ] No API keys, tokens, or secrets in plain text

## Resource Group and Cost Management

All resources live in a dedicated resource group: `<your-resource-group>`

## Deployed

Project: `<your-foundry-project>` in Foundry resource `<your-foundry-resource>` (Sweden Central, resource group `<your-resource-group>`). Endpoint stored locally via `dotnet user-secrets set FOUNDRY_PROJECT_ENDPOINT ...`.

| Lab | Deployment name | Base model | Lifecycle | Context (in / out) |
| --- | --- | --- | --- | --- |
| OpenAI | `gpt-5.4-1` | `gpt-5.4` (2026-03-05) | GA, Global Standard | 200k / 100k |
| DeepSeek | `DeepSeek-R1-0528-1` † | `DeepSeek-R1-0528` (2025-05-28) | Preview, Direct from Azure | 128k / 4k |

Both are in Microsoft's [Responses-API supported list](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/generate-responses#list-of-supported-models), which is the dealbreaker check — only that tier binds to `OpenAIFileClient.UploadFileAsync` (`purpose=Assistants`) + `ResponseTool.CreateCodeInterpreterTool` with `CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(fileIds: …)`.

† Reaches Foundry but cannot complete the production analytics prompt — see [DeepSeek-R1-0528-1 runtime failure](#deepseek-r1-0528-1-runtime-failure) below.

### DeepSeek-R1-0528-1 runtime failure

Confirmed 2026-04-26 via the WPF Compare Models flow. Against the full analytics prompt the run terminates with `ResponseStatus.Incomplete` and the agent throws (`CodeInterpreterFundAnalyticsAgent.cs:155-159`). Consistent with the model card's `Tool calling: No`: DeepSeek accepts the Code Interpreter tool definition but never invokes it, so without file access the reasoning loop chews through `max_output_tokens` and gets capped as Incomplete. Token usage shows `0 / 0` in the panel because the throw happens before usage is recorded on the `FundAnalyticsRun`.

The toy integration test in [`CodeInterpreterFundAnalyticsAgent_DeepSeek_IntegrationTests`](../FikaFinans.Infrastructure.Tests/Foundry/CodeInterpreterFundAnalyticsAgent_DeepSeek_IntegrationTests.cs) does not reproduce this — its small prompt ("how many rows does positions.csv have?") completes without needing tool calls, so DeepSeek just guesses and returns `Completed`. The failure mode only surfaces under the production prompt.

Kept in `FoundryModelIds.ComparisonModels` for now as a documented failure case: useful contrast in the portfolio narrative — *"Responses-API tier ≠ Tool calling: Yes"*. Candidate for removal in v2 if the contrast stops earning its keep.

## Rejected candidates

### Anthropic — Claude (`claude-opus-4-7`, `claude-sonnet-4-6`, ...)

Foundry hosts Claude under the partner catalog through the **Messages API** at `<resource>.services.ai.azure.com/anthropic/v1/messages`, not the Responses API. The plan's file-upload + Code Interpreter pipeline is bound to the Responses tier, and zero Claude variants appear in the [Responses-API supported list](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/generate-responses#list-of-supported-models) (re-verified 2026-04-26). Wiring Claude would require a parallel SDK path with Anthropic's own Files and code-execution APIs — violates the "one adapter, one upload pipeline" rule in [plan.md](plan.md). Excluded despite being a top-tier reasoning lab.

### xAI — Grok (every variant tried)

- `grok-4` — gated behind "Microsoft eligibility criteria" approval; not granted to personal Pay-as-you-go on attempt.
- `grok-4-fast-reasoning` — deprecated 2026-04-01.
- `grok-4-20-reasoning` (Grok 4.2) — Preview only, model card omits the Responses tag, and the lineage above had already burned the time budget for this slot.

### Microsoft — `MAI-DS-R1`

**Retired 2026-02-27.** Listed on [Retired Foundry Models](https://learn.microsoft.com/azure/foundry/openai/concepts/retired-models#microsoft) with replacement guidance "Any DeepSeek model available in the Model catalog." No longer deployable, regardless of any other criteria.

Originally rejected on portfolio-narrative grounds even before retirement: Microsoft's deterministic post-train of DeepSeek-R1 — pairing it with a sibling DeepSeek deployment would compare two tunings of the same base model rather than two different lab voices.

### Skipped for v1 (no blocker, just scope)

`Llama-4-Maverick-17B-128E-Instruct` (Meta), `Kimi-K2.6` (Moonshot AI), `Mistral-Large-3` — all in the Responses-API tier, all viable. Held back to keep the v1 lineup focused on two reliably-shippable lab voices.

## Adding a third (or N-th) model later

The architecture (`IEnumerable<IFundAnalyticsAgent>` + `Task.WhenAll`) is N-model-ready. Adding one is mechanical:

1. **Verify** the candidate, in this order — stop at the first failure:
   - **Not retired.** Check [Retired Foundry Models](https://learn.microsoft.com/azure/foundry/openai/concepts/retired-models). The Foundry capability matrices don't filter retired entries out — a model can still appear "supported" months after its retirement date (e.g., `MAI-DS-R1`, retired 2026-02-27).
   - **Tool calling actually works.** The model card on [Models sold directly by Azure](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) is authoritative — the `Tool calling` column is the field to read. The [tool-best-practice matrix](https://learn.microsoft.com/azure/foundry/agents/concepts/tool-best-practice) has been observed to mark `Code Interpreter: Yes` for models whose own card says `Tool calling: No` (e.g., `DeepSeek-R1-0528`); those models will accept the Code Interpreter tool but never actually invoke it — they exhaust their budget reasoning about what code they would write.
   - **In the [Responses-API supported list](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/generate-responses#list-of-supported-models).** Without this tier, the OpenAI Files API + `ResponseTool.CreateCodeInterpreterTool` pipeline won't bind.
2. **Deploy** in the Foundry portal under `<your-foundry-project>`. Note the auto-generated deployment name (the new portal does not let you set it during deploy).
3. **Add** the deployment name to `FoundryModelIds.cs`.
4. **Register** one more `CodeInterpreterFundAnalyticsAgent` in `InfrastructureModule.cs`.
5. **Bind** a new `ModelRunViewModel` column in the Compare Models view.

`Llama-4-Maverick-17B-128E-Instruct` is the prime v2 candidate for the Meta-lab voice slot.
