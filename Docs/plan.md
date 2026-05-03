# FikaFinans — Multi-Model Foundry Fund-Analytics Plan

> **Status:** Implemented (2026-04). The architecture, UX rules, and rationale below are still load-bearing, but **specific SDK references in this doc are historical** — the implementation migrated off the classic `Azure.AI.Agents.Persistent` path onto `Azure.AI.Projects` 2.0 + `Azure.AI.Projects.Agents` + the Responses API + `OpenAIFileClient` (`purpose=Assistants`). For the current SDK surface and hard contracts, read [../CLAUDE.md](../CLAUDE.md) and the actual code. Specifically outdated here: `Azure.AI.Agents.Persistent` package add, `PersistentAgentsClient`, `Files.UploadFileAsync` with `PersistentAgentFilePurpose.Agents`, `CodeInterpreterToolDefinition` + `ToolResources`, and `_aiProjectClient.AsAIAgent(...)`.

## Context

Already working as a **Claude.ai Project**: the 4 data files (`summary.csv`, `metadata.csv`, `positions.csv`, `portfolio_structure.md`) are attached to a project, the system prompt at [prompt_analytics.md](prompt_analytics.md) is the project instructions, questions run over the attached files. Files refresh ~weekly; questions happen many times in between.

This plan **ports that workflow to Azure AI Foundry** so the same prompt can run through **multiple full reasoning models in parallel** for side-by-side comparison. Portfolio/skills-demo piece — exercises Microsoft Agent Framework + Azure AI Foundry **Code Interpreter** and shows multi-model comparison in the WPF app.

### Hard input/output contract

- **Inputs:** the 4 files in [FikaFinans/Docs/](.) (read from disk before each run) + the user's question + the system prompt embedded as resource.
- **Outputs:** raw text response from each model + token counts + elapsed time. No structured-JSON contract — these are reasoning models talking to a human, free-form Markdown is fine.
- **Files travel via the Foundry Files API only.** Uploaded with `purpose=agents`, referenced by `fileId` in `ToolResources.CodeInterpreter.FileIds`. **Never inline file contents into the prompt text** — that defeats the point (token blow-up, no Code Interpreter, no skills demo).
- **Folder is the source of truth.** Before each run, scan the folder; for any file whose `LastWriteTimeUtc` is newer than the cached upload, re-upload via API and update the sidecar. No manual portal uploads, ever.

The existing FikaFinans solution is a clean DDD skeleton (4 empty class libraries + WPF + tests). The reference implementation pattern lives in [../../backend/KanelBrief.Functions](../../backend/KanelBrief.Functions/) (`Microsoft.Agents.AI.Foundry` + `AIProjectClient.AsAIAgent` + ephemeral analyzer pattern).

## Decisions

- **Run target:** Wire into the WPF app. Agent code in `FikaFinans.Infrastructure`, UI tab in `FikaFinans.Wpf`.
- **Foundry tool:** Code Interpreter (Python/pandas in a sandboxed container). The agent does all data work itself — no C# pre-aggregation, no manual RAG, no `FundDataAggregator` class. If a candidate model can't use Code Interpreter on Foundry, swap that model out; don't write a software substitute for the missing capability.
- **Foundry-only.** Every model in the comparison must be reachable via `Microsoft.Agents.AI.Foundry` + `AIProjectClient.AsAIAgent` and must support the Foundry **Code Interpreter** tool per the [tool-support-by-model matrix](https://learn.microsoft.com/azure/foundry/agents/concepts/tool-best-practice#tool-support-by-region-and-model). No second SDK path (no Anthropic, no Bedrock, no direct OpenAI). One adapter, one upload pipeline, one sidecar.
- **Models — three in parallel, three different labs:**
  - **gpt-5.4** (OpenAI / Microsoft) — full reasoning, already deployed in the KanelBrief Foundry project.
  - **Grok 4** (xAI) — full reasoning, in the Foundry tool matrix with Code Interpreter ✅ + File Search ✅. Different lab, useful contrarian voice.
  - **DeepSeek-R1** (DeepSeek) — open-weights reasoning, in the matrix with Code Interpreter ✅. Already deployed.
  Three labs (OpenAI, xAI, DeepSeek), one SDK path. All three reuse the same `CodeInterpreterFundAnalyticsAgent` adapter — only the `ModelId` constant changes per DI registration.

## Architecture

```text
FikaFinans.Domain          ← entities (FundAnalyticsRun, ModelComparison)
FikaFinans.Application     ← ports (IFundAnalyticsAgent, IFoundryFileStore, IPromptProvider) + use case
FikaFinans.Infrastructure  ← Foundry adapters (one Code-Interpreter agent class, parameterised by model id)
FikaFinans.Wpf             ← "Compare Models" tab, ViewModel orchestrating parallel runs
```

Mirrors the KanelBrief separation: Application owns the abstractions, Infrastructure owns the Azure SDK glue.

### File uploads — implicit + explicit

There is no "project attachment" concept on Foundry the way Claude.ai has. **The WPF program is responsible for uploading files** to Foundry via the Files API. Two complementary paths, both go through `IFoundryFileStore`:

- **Implicit (auto)** — `EnsureFilesUploadedAsync` runs at the start of every comparison: scans the data folder, compares local `File.GetLastWriteTimeUtc(path)` to the cached `sourceMtime`, uploads any file that's `Stale` or `NotUploaded`. Silent. Net effect: the user can drop new CSVs in the folder and just hit `Run` — uploads happen as a side effect.
- **Explicit (manual)** — the upload strip in the WPF view shows current status and exposes per-row `↻` and a global `Refresh all` button. Calls `ForceReuploadAllAsync` (or single-file equivalent), which deletes the existing fileId via `Files.DeleteFileAsync` and re-uploads regardless of mtime. Used when the user knows a file's contents changed but the mtime didn't (e.g. file copied with `-p`), or just wants confidence before a run.

Mechanics:

- Upload via `PersistentAgentsClient.Files.UploadFileAsync(path, PersistentAgentFilePurpose.Agents)` → returns `fileId`. The local logical name (`summary.csv` etc.) is passed as the upload's `name` argument.
- Persist `{logicalName → (fileId, sourceMtime, sourceSize, uploadedAt)}` in a JSON sidecar at `%APPDATA%\FikaFinans\foundry-files.json`. Sidecar is the source of truth for "what's currently uploaded."
- No vector store — Code Interpreter consumes file IDs directly via `CodeInterpreterToolDefinition` + `ToolResources.CodeInterpreter.FileIds`.
- Foundry files don't auto-expire on the agents service. Old fileIds are deleted explicitly when their content is replaced; stale fileIds left over from a crashed run are cleaned up on the next `ForceReuploadAllAsync`.

### Data folder + hardcoded filenames

The data location is **a single folder configured in settings** (see "Settings" below). The filenames inside that folder are **hardcoded constants** matching what the prompt expects:

```csharp
internal static class FundDataFiles
{
    public const string Summary   = "summary.csv";
    public const string Metadata  = "metadata.csv";
    public const string Positions = "positions.csv";
    public const string Structure = "portfolio_structure.md";
}
```

Resolution: `Path.Combine(settings.DataFolder, FundDataFiles.Summary)` etc. The `FundDataFileSet` runtime object holds the four resolved absolute paths; it's built once at startup from the settings value. If a file is missing the agent run fails fast with a clear `"missing summary.csv in {folder}"` error — no fallback, no fuzzy matching.

The user is responsible for placing files with the canonical names in that folder (i.e. drop `YieldRaccoon_summary_…csv` in and rename to `summary.csv`). The current `Docs/` files would need renaming once before first use — or `Docs/` stays as documentation and the data folder points elsewhere (e.g. `%USERPROFILE%\OneDrive\FikaFinans\data`). Default at first launch: the project's `Docs/` folder, but only as a starting point — user is expected to point it at their real export location.

## Settings

Persistent app settings, surfaced via a settings button in the **window header** (MahApps `WindowCommands` area, top-right of the title bar — a gear icon).

- **Storage:** `%APPDATA%\FikaFinans\settings.json` — small JSON, manually editable if needed. Schema:

  ```json
  { "dataFolder": "C:\\path\\to\\folder" }
  ```

- **UI:** clicking the gear opens a `MetroDialog` (or simple modal `Window`) with one row: `Data folder: [text box]  [Browse…]  [OK]  [Cancel]`. `Browse…` opens `Microsoft.Win32.OpenFolderDialog` (built-in to .NET 9 WPF). Validation: folder must exist; soft-warn if any of the 4 hardcoded files are missing (don't block — they may be added later).
- **Apply on restart is fine.** The settings dialog writes the JSON and shows a "Restart to apply" message. No hot-reload, no `IOptionsMonitor` wiring. The Foundry file store reads `settings.json` once on startup; if the folder changed since the last run, the sidecar's `sourceMtime` checks will detect file changes and re-upload via API on the next agent run automatically (no special handling needed for "folder changed").
- **First-run default:** if no `settings.json` exists, default `dataFolder` to the application install path's `Docs\` (or `AppContext.BaseDirectory`); the gear icon glows / shows a tooltip prompting the user to point it at their real folder.

Settings live in `FikaFinans.Application/Settings/AppSettings.cs` (the record) + `FikaFinans.Infrastructure/Settings/JsonAppSettingsStore.cs` (the loader/writer). The Wpf project owns the dialog view + viewmodel.

## Files to add

### `FikaFinans.Domain`

- `Models/FundAnalyticsRun.cs` — `{ ModelId, Question, ResponseText, InputTokens, OutputTokens, ElapsedMs, RanAtUtc }`
- `Models/ModelComparison.cs` — `{ Question, IReadOnlyList<FundAnalyticsRun> Runs }`

### `FikaFinans.Application`

- `Agents/IPromptProvider.cs` — `string GetFundAnalyticsPrompt()` (mirror [../../backend/KanelBrief.Core/Agents/IPromptProvider.cs](../../backend/KanelBrief.Core/Agents/IPromptProvider.cs))
- `Agents/IFundAnalyticsAgent.cs` — `Task<FundAnalyticsRun> RunAsync(string question, CancellationToken)`; one instance per model
- `Agents/IFoundryFileStore.cs` — port for the upload pipeline. Three methods:
  - `Task<IReadOnlyList<FoundryFileEntry>> GetStatusAsync(FundDataFileSet, CancellationToken)` — for each logical file: `LogicalName`, `LocalPath` exists?, `LocalMtime`, `LocalSize`, `UploadedFileId` (or null), `UploadedAt`, derived `Status` enum (`Missing` / `NotUploaded` / `Stale` / `Fresh`). Pure inspection — no API calls beyond reading the local sidecar.
  - `Task<IReadOnlyDictionary<string, string>> EnsureFilesUploadedAsync(FundDataFileSet, CancellationToken)` — implicit path: uploads anything `Stale` or `NotUploaded`, returns `logicalName → fileId` for use by an agent run.
  - `Task<IReadOnlyDictionary<string, string>> ForceReuploadAllAsync(FundDataFileSet, IProgress<FoundryFileEntry>?, CancellationToken)` — explicit path: deletes existing fileIds via `Files.DeleteFileAsync` and re-uploads everything regardless of mtime, reporting progress per file. Wired to the **Refresh uploads** button.
- `Agents/FoundryFileEntry.cs` — record returned by the store, used directly by the WPF status strip.
- `Agents/FundDataFileSet.cs` — config record above
- `UseCases/CompareModelsUseCase.cs` — accepts `IEnumerable<IFundAnalyticsAgent>`, calls each via `Task.WhenAll`, returns `ModelComparison`

### `FikaFinans.Infrastructure`

NuGet additions on the Infrastructure csproj:

- `Microsoft.Agents.AI.Foundry` 1.0.0
- `Azure.AI.Projects` 2.0.0
- `Azure.AI.Extensions.OpenAI` 2.0.0
- `Azure.AI.Agents.Persistent` 1.1.0 *(new vs KanelBrief — needed for `Files`, `CodeInterpreterToolDefinition`, `ToolResources`)*
- `Azure.Identity` 1.20.0

Files:

- `Foundry/FoundryFileStore.cs` — implements `IFoundryFileStore`. Constructed with `PersistentAgentsClient`. Reads/writes the JSON sidecar. Singleton lifetime.
- `Foundry/CodeInterpreterFundAnalyticsAgent.cs` — **the only agent implementation**, parameterised at construction with `ModelId` (`"gpt-5.4"`, `"grok-4"`, `"DeepSeek-R1"`). On `RunAsync`: ensure files via `IFoundryFileStore`, build `CodeInterpreterToolDefinition` + `ToolResources` from the returned file IDs, create ephemeral agent via `_aiProjectClient.AsAIAgent(model: ModelId, instructions: systemPrompt, tools: [codeInterpreterTool], toolResources: ...)`, run with `ChatClientAgentRunOptions { MaxOutputTokens = 4000 }`, capture `response.Usage` tokens. Pattern mirrors [AzureWeeklySummaryAnalyzer.cs:18](../../backend/KanelBrief.Functions/Agents/Analyzers/AzureWeeklySummaryAnalyzer.cs#L18). Three instances are registered in DI — one per model.
- `Prompts/EmbeddedPromptProvider.cs` — embeds `prompt_analytics.md` via `<EmbeddedResource Include="..\Docs\prompt_analytics.md" Link="Prompts\fund_analytics.prompt.md" />` in the csproj. Loads as a resource at runtime.
- `DependencyInjection/InfrastructureModule.cs` — Autofac module registering `AIProjectClient`, `PersistentAgentsClient`, `FoundryFileStore` (singleton), and three `CodeInterpreterFundAnalyticsAgent` instances keyed by model name. Also registers `CompareModelsUseCase`.

### `FikaFinans.Wpf`

- `Views/CompareModelsView.xaml` — MahApps `MetroContentControl`. Vertical layout, top to bottom:
  1. **File upload strip** — bound to `Files` collection. One row per logical file (4 rows): icon + `summary.csv` + status badge (`Missing` / `Not uploaded` / `Stale` / `Fresh`) + `last uploaded N min ago` + per-row `↻` button. Trailing `Refresh all` button on the right. Stale/Missing rows render in an attention color so the user can see at a glance whether to refresh before asking.
  2. **Question entry** — multiline `TextBox` + `Run on all models` button.
  3. **Three-column results `Grid`** — one `ModelResultPanel` per model, bound to `Run1`/`Run2`/`Run3`.
  Settings gear lives in the window header `WindowCommands` (see Settings §). Markdown rendering deferred.
- `Views/ModelResultPanel.xaml` — small reusable `UserControl` for a single model's pane (model-name header, status indicator, live elapsed counter, token counts, scrollable read-only response, `Copy response` button). Bound to one `ModelRunViewModel`. Keeps the 3 columns identical and avoids XAML duplication.
- `Views/FileUploadRow.xaml` — small reusable `UserControl` for one row of the upload strip. Bound to a `FileEntryViewModel`.
- `ViewModels/CompareModelsViewModel.cs` — DevExpressMvvm `ViewModelBase`. Bindable: `Files` (ObservableCollection of `FileEntryViewModel`), `Question`, `IsRunning`, `Run1`, `Run2`, `Run3`, `ErrorMessage`. Commands: `RunComparisonCommand`, `RefreshAllFilesCommand`, `RefreshOneFileCommand`. On view activation: `IFoundryFileStore.GetStatusAsync` populates `Files`. `RunComparisonCommand` calls `EnsureFilesUploadedAsync` first (implicit path catches anything stale), then `CompareModelsUseCase`.
- `ViewModels/FileEntryViewModel.cs` — per-file UI state: `LogicalName`, `Status`, `LocalMtime`, `UploadedAt`, `IsBusy` (true while a refresh is in flight for this row), `RefreshThisCommand`. Subscribes to a status stream from the store so progress reports from `ForceReuploadAllAsync` flow into individual rows independently.
- `ViewModels/ModelRunViewModel.cs` — per-panel state: `ModelId`, `Status` (enum: Idle/Running/Done/Error), `ElapsedSeconds` (live, updates while running), `InputTokens`, `OutputTokens`, `ResponseText`, `ErrorText`. Uses `System.Reactive` for the elapsed-second tick stream and to project per-model task completion into independent state changes — one model finishing or failing must not block the others.
- Wire the new tab into the existing main shell (read [FikaFinans.Wpf/MainWindow.xaml](../FikaFinans.Wpf/MainWindow.xaml) before adding — exact integration depends on the shell layout from the `cb2b17f` skeleton commit).
- DI: extend the existing Autofac container to load `InfrastructureModule`. Foundry endpoint comes from `dotnet user-secrets` under key `FOUNDRY_PROJECT_ENDPOINT` (same as KanelBrief).

## UX / UI design

Apply during implementation, not now. Use the relevant Claude skills:

- **`dotnet-wpf-mvvm`** — for the architectural side: ViewModels, ICommand wiring, DevExpress MVVM `ViewModelBase`, Autofac DI, Rx.NET subscriptions in `ModelRunViewModel`. The existing app already uses this stack (DevExpressMvvm 24.1.6, Autofac 9, System.Reactive 6.1) so the patterns from this skill drop in directly.
- **`wpf-fluent-design`** — for the visual layer: MahApps.Metro Fluent theme, control styling, color/brush selection, spacing/typography, `DataTemplate` shapes, status-indicator visuals. Gear icon in `WindowCommands`, panel borders, focus rings, busy spinners — all from this skill's guidance.
- **`dotnet-reactive-patterns`** — for the per-model independent state streams in `ModelRunViewModel` (elapsed-second tick, completion observable, error projection). Uses `IObservable` + `CompositeDisposable` per the existing project conventions.

UX rules to apply when implementing:

- **Fail per model, not per run.** If gpt-5.4 fails, Grok 4 and DeepSeek-R1 still complete and render. Each panel owns its own error state and an inline `Retry` button.
- **Live progress.** While a model is running its panel shows an animated busy state plus `00:42 elapsed`. Code Interpreter sessions take 60–120 s, so silent waiting is unacceptable — the user must see motion.
- **No blocking modal during runs.** The whole window remains interactive; another tab can be used while runs are in flight (cancellation token cancels all 3 if user navigates away — TBD whether to actually cancel or let them finish in the background).
- **Question entry stays editable while running.** Don't disable it. User can refine the next question while waiting.
- **Layout breakpoint.** 3 panels need ~1500 px to read comfortably. Below that (rare for desktop, but happens on laptop screens) collapse to a vertical stack via a `MultiBinding` on `ActualWidth`. Don't introduce horizontal scroll on the panels themselves — text lines stay readable, only the panel column wraps.
- **Copy-out friendly.** Response panes are read-only `TextBox` (not `TextBlock`) so Ctrl+A / Ctrl+C work natively. Each panel has a `Copy response` button as well.
- **Match existing shell theme.** Don't introduce a new accent color — use whatever the WPF skeleton's MahApps theme is set to. Read [FikaFinans.Wpf/App.xaml](../FikaFinans.Wpf/App.xaml) first.
- **Settings gear placement.** `WindowCommands` (top-right of the title bar, MahApps convention), single icon, opens the Settings dialog described in the Settings § above. No menu bar, no separate settings tab — that's overkill for one setting.

### `FikaFinans.Infrastructure.Tests`

- Integration tests gated on env var `FOUNDRY_PROJECT_ENDPOINT` (mirror [../../backend/KanelBrief.Functions.Tests/Integration/Analyzers/](../../backend/KanelBrief.Functions.Tests/Integration/Analyzers/)).
- `FoundryFileStoreIntegrationTests.cs` — round-trip: upload 4 files, second call returns same IDs, mutate one source mtime → re-uploads only that file.
- `CodeInterpreterFundAnalyticsAgentIntegrationTests.cs` — one test per registered model. Ask: *"What's the Sharpe of the highest-Sharpe satellite fund over the last 3 windows?"* Assert response is non-empty and contains a numeric answer.

## Reuse from KanelBrief

- **Prompt loading** — copy [PromptFileParser.cs](../../backend/KanelBrief.Core/Agents/PromptFileParser.cs) verbatim into `FikaFinans.Application/Agents/`. Handles YAML frontmatter and CRLF/LF.
- **DI shape** — [Program.cs:81-104](../../backend/KanelBrief.Functions/Program.cs#L81-L104) shows `AIProjectClient` + `DefaultAzureCredential` registration. WPF uses Autofac instead of `IServiceCollection`, but the construction calls are identical.
- **Token capture** — `response.Usage.InputTokenCount / OutputTokenCount` (same as `AzureWeeklySummaryAnalyzer.cs:59`).
- **AzureCredential** — `DefaultAzureCredential` works for local WPF runs because the Azure CLI is installed; no managed identity needed for desktop.

## Verification

1. **Spike each of the three models against Code Interpreter** before wiring all three into the WPF tab. For each `ModelId` in `{ "gpt-5.4", "grok-4", "DeepSeek-R1" }`: a one-file proof using `Azure.AI.Agents.Persistent` 1.1.0 directly — upload `summary.csv`, attach Code Interpreter, ask the model to compute "mean of `total_return_pct` column." Any model that fails (tool not honoured / agent creation rejects the tool) gets swapped for another Foundry model from the same tool matrix (e.g. gpt-5, o3, grok-4-fast-reasoning, MAI-DS-R1). **Do not** write a C# fallback path; swap the model.
2. `dotnet build FikaFinans/FikaFinans.sln` — clean compile.
3. With `FOUNDRY_PROJECT_ENDPOINT` set: `dotnet test FikaFinans.Infrastructure.Tests` — all green.
4. Launch the WPF app, open the **Compare Models** tab, paste: *"Which fund should I sell first this week and why?"*
   - All three panels populate within ~60–120 s each (Code Interpreter sessions are slower than plain chat).
   - Each panel shows live elapsed-second counter while running, then token counts when done.
   - Per-model failure isolation works: if one panel errors, the other two still render their answer.
   - Sanity check: no model invents a fund name not present in `positions.csv`.
   - Re-run a second question without restarting → file uploads are skipped (verify via logs that cached fileIds are reused).

## Estimated cost

- Code Interpreter session: ~$0.03 per session boot + per-token, per agent. Each WPF "Run" = **3 sessions** (one per model).
- Weekly file refresh: 4 small uploads, negligible.
- No new Foundry resource — reuses the existing KanelBrief project, so zero idle cost added.

## Out of scope (do later)

- Persisting comparison runs to Azure Tables (KanelBrief has the repository pattern; lift it later if history is wanted).
- Markdown rendering in the WPF response panes — start with plain `TextBox`.
- Cross-model "judge" agent that scores the two answers — natural Phase 2.
- Hooking into FikaFinans.Domain/Application use cases beyond the comparison flow.

## Open questions for next planning round

- Three-model spike outcome: DeepSeek-R1 + Code Interpreter and Grok 4 + Code Interpreter both need verification (see Verification §1). Any swaps go in here next round.
- Should the WPF tab persist the last N comparisons locally (SQLite via EF Core, like the rest of the desktop app) or stay stateless until a later phase?
- Where does the question history / saved questions live? Per-tab in-memory only, or a small file in `%APPDATA%`?
- Cancellation semantics: when the user navigates away from the **Compare Models** tab while runs are in flight, do we cancel the agents (and lose tokens already spent) or let them complete in the background and update the panels later?
- Should the model list itself be settings-driven (so a user can disable Grok and add o3 without rebuilding), or does a fixed compile-time trio stay simpler for v1?
