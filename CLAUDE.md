# CLAUDE.md

## 🎭 Communication Style

This is a personal hobby project. Be warm, friendly, and human — like a coding buddy, not a corporate assistant. Humor is welcome and encouraged. Use casual language, share enthusiasm about cool solutions, crack a joke when the moment calls for it, and celebrate wins together. Skip the robotic "I'll help you with that" phrasing — just be yourself and have fun building things.

**When you feel stuck or overwhelmed:** Don't spiral into desperation — just ask for guidance. This is a hobby project, we're here to have fun and learn together. There's no pressure, no deadlines, no stakes worth stressing over. If a task feels unclear or too complex, say so and we'll figure it out together.

## Project Goals

This project is a **skills portfolio** — the primary goal is to demonstrate proficiency in:

- **Microsoft Agent Framework (MAF)** — the provider-agnostic agent abstraction (`Microsoft.Agents.AI`)
- **Azure AI Foundry** — model hosting + the Foundry Agents Service, accessed via `Azure.AI.Projects` (GA 2.0)

MAF sits *on top of* AI Projects for the Foundry case (`AIProjectClient.AsAIAgent(...)`). They aren't competing choices — MAF is the wrapper, AI Projects is the underlying SDK. The portfolio narrative covers both layers.

### ⚠️ Microsoft Agent Framework — Knowledge Freshness Warning

MAF and the Foundry Agents Service are evolving rapidly (MAF is in 1.0 RC as of March 2026; the classic Persistent Agents path is retiring 2027-03-31). Claude's training data lags. **Before answering questions or generating code related to the Agent Framework or Foundry agents, always check the latest Microsoft Learn documentation via MCP docs tools.** Do not rely solely on training knowledge — verify against current docs first.

## Naming Conventions

Two formats, two community conventions — they don't try to match. Stick to each format's idiom:

| Format                                              | Convention   | Example                                            |
| --------------------------------------------------- | ------------ | -------------------------------------------------- |
| **JSON** (wire / API responses, in-memory POCOs)    | `camelCase`  | `periodStart`, `weeklySummaryRunId`, `reportType`  |
| **YAML** (markdown frontmatter in `analytics-*.md`) | `snake_case` | `period_start`, `iso_week`, `report_type`          |

The .NET side uses `KanelBrief.Core.Serialization.KanelJsonOptions.CamelCase`
(PascalCase C# properties → camelCase wire). YAML keys are emitted by
[`ReportExportMarkdownFormatter`](FikaForecast/FikaForecast.Application/Services/ReportExportMarkdownFormatter.cs)
and consumed by the FikaFinans agent — they follow YAML idiom, not JSON.

The semantic mapping is trivial (`period_start` ↔ `periodStart`); don't try to unify the casing across formats.

## Key Technologies

### Desktop (existing)

| Technology | Version | Purpose |
| ------------ | --------- | --------- |
| .NET | 9.0 | Framework |
| WPF | - | Desktop UI |
| Entity Framework Core | 9.0 | SQLite persistence |
| DevExpressMvvm | 24.1.6 | MVVM framework |
| Autofac | 9.0.0 | Dependency injection |
| Rx.NET | 6.1.0 | Reactive programming |
| MahApps.Metro | 2.4.11 | Modern UI toolkit |
| WebView2 | 1.0.2903 | Embedded Chromium browser |
| Magick.NET | 14.10.2 | Privacy filter image processing |
| NLog | 6.0.7 | Logging |

### Frontend

| Technology | Purpose |
| ------------ | --------- |
| Next.js | Web frontend |

### Cloud / AI

| Technology | Purpose |
| ------------ | --------- |
| Azure Functions | Serverless compute (individual task handlers) |
| Azure Tables | NoSQL key-value storage |
| Azure AI Foundry | Model hosting + Foundry Agents Service |

### .NET SDK packages for Foundry agents

| Package | Status | Role |
| --------- | -------- | ------ |
| `Azure.AI.Projects` | **GA 2.0** | `AIProjectClient`, agent administration, the Foundry SDK foundation |
| `Azure.AI.Extensions.OpenAI` | Active | Bridge to the OpenAI .NET SDK — provides `OpenAIFileClient` for `purpose=Assistants` uploads, `ProjectResponsesClient` for the Responses API |
| `Microsoft.Agents.AI` | **1.0 RC** | MAF — the provider-agnostic `AIAgent` abstraction. Layer on top of AI Projects via `.AsAIAgent(...)` |
| `Azure.Identity` | GA | `DefaultAzureCredential` for keyless auth (`az login` locally) |
| `OpenAI` | GA | Official OpenAI .NET SDK, pulled in transitively by `Azure.AI.Extensions.OpenAI` |

**Default migration target:** new code should target `Azure.AI.Projects` directly, then optionally wrap with MAF (`Microsoft.Agents.AI`) when the abstraction earns its keep (multi-provider, workflows, middleware). For the Foundry+CodeInterpreter+file-upload scenario specifically, AI Projects has a published, working C# sample; MAF's coverage of that pattern is still catching up.

**Don't use:**

- `Azure.AI.Agents.Persistent` / `PersistentAgentsClient` — the *classic* Foundry Agents path. **Retiring 2027-03-31.** Existing code on this SDK is on borrowed time and should be migrated.
- `Microsoft.SemanticKernel.*` — deprecated for this org's projects.

### Additional Guidance

- Keep costs low: When suggesting infrastructure, prioritize free/low-cost options (Azure free tier, free APIs). Only suggest paid upgrades if strictly necessary and mention the cost impact.
- **Azure App Service F1 (free tier) is already in use** for the backend — do not suggest removing or replacing it. Use Azure Functions for new serverless workloads instead.
- **Do not use or reference Semantic Kernel** — it is deprecated. Use Microsoft Agent Framework (MAF) instead, layered on `Azure.AI.Projects` for the Foundry case.

## Development Environment

- **IDE:** Visual Studio 2022 **Community**. When documenting paths to VS-bundled
  tools, hardcode `Community` — don't write `<edition>` placeholders.
  Example: Azurite lives at
  `C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\Microsoft\Azure Storage Emulator\azurite.exe`.
- **VS Code is NOT used for this project** — all instructions should assume Visual Studio 2022 Community only.
- **OS:** Windows 11.
- **Shell:** Git Bash (use Unix-style forward-slash paths and `/dev/null`, not `NUL`).

## Project Overview

**Project Hosting:**

- **Repository:** GitHub (personal, public)
- **Deployment:** Azure (private infrastructure)
- **Services:** Azure App Service F1 (backend), Azure Static Web Apps (frontend), Azure Functions (serverless), Azure AI Foundry, Application Insights, Key Vault

### Shared Backend

This project shares a backend with [SemanticKernel-FundDocsQnA-dotnet-nextjs](https://github.com/Muhomorik/SemanticKernel-FundDocsQnA-dotnet-nextjs).

**Backend stack:** ASP.NET Core 9 Web API with a hybrid Q&A system:

- **RAG Pipeline** — Semantic search over PDF fund documents (OpenAI embeddings)
- **Function Calling** — Structured queries against Azure SQL via AI plugins
- **LLM:** OpenAI gpt-4.1-mini (default) or Groq llama-3.3-70b-versatile (free alternative)
- **Embeddings:** OpenAI text-embedding-3-small
- **Vector Storage:** InMemory (default) or Azure Cosmos DB (persistent)
- **Database:** Azure SQL (optional — enables function calling; without it, RAG-only mode)
- **Monitoring:** Application Insights
- **Secrets:** Azure Key Vault (production), `dotnet user-secrets` (local)

**Key endpoints:** `POST /api/ask` (main Q&A), `POST /api/ask/stream` (streaming), fund management & health endpoints. Swagger UI at `/swagger`.

**Architecture:** Domain-Driven Design with bounded contexts. The LLM autonomously routes between RAG and function calling (or combines them for hybrid answers).

## Documentation

| Document | Description |
| ---------- | ------------- |
| [Azure Deployment](docs/AZURE-DEPLOYMENT.md) | AI Foundry setup, Bing Grounding, model deployments, cost tracking |
| [Secrets Management](docs/SECRETS-MANAGEMENT.md) | User secrets, Key Vault, API keys |
| [FikaForecast README](FikaForecast/README.md) | App overview, pipeline, model comparison, roadmap |

## Build & Run Commands

## Architecture

- **Domain-Driven Design** with bounded contexts

### Data Flow

### Key Services

### Configuration

## Important Notes

### Documentation Security

When editing files in `docs/`, AI assistants MUST verify before saving:

- [ ] No real Azure resource names — use `<your-...>` placeholders
- [ ] No real endpoint URLs — use `<your-...>` placeholders
- [ ] No API keys, tokens, or secrets in plain text

## Testing Guidelines

Complete testing guidelines have been moved to the `dotnet-unit-testing-nunit` skill.

**Quick Reference:**

- Use NUnit + AutoFixture + AutoMoq for all .NET tests
- Always resolve SUT from AutoFixture (never `new`)
- Follow AAA pattern (Arrange, Act, Assert)
- Test naming: `MethodName_Scenario_ExpectedBehavior`
- Mock all external dependencies

For detailed patterns, examples, and advanced techniques, see `.claude/skills/dotnet-unit-testing-nunit/SKILL.md`
