using Autofac;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.Application.Agents;
using FikaFinans.Application.Bank;
using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Application.Pipeline.Llm;
using FikaFinans.Application.Settings;
using FikaFinans.Application.UseCases;
using FikaFinans.Application.Schedules;
using FikaFinans.Infrastructure.Bank;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Paths;
using FikaFinans.Infrastructure.Pipeline.Agents;
using FikaFinans.Infrastructure.Pipeline.Llm.Foundry;
using FikaFinans.Infrastructure.Prompts;
using FikaFinans.Infrastructure.Schedules;
using FikaFinans.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NLog;

namespace FikaFinans.Infrastructure.DependencyInjection;

/// <summary>One required configuration key that wasn't found at startup.</summary>
public sealed record MissingConfiguration(string Key, string Hint);

/// <summary>
/// Autofac module that wires Azure SDK clients, the Foundry file store, one
/// Code-Interpreter fund-analytics agent per deployed model, and the comparison
/// use case. Adding/removing a model is a one-line change in
/// <see cref="FoundryModelIds.ComparisonModels"/> — see <c>Docs/models.md</c>.
/// </summary>
/// <remarks>
/// The Foundry endpoint is read from configuration key <c>FOUNDRY_PROJECT_ENDPOINT</c>
/// (user-secrets locally, App Service config in Azure). Mirrors KanelBrief's setup.
/// When the endpoint is missing the module still registers everything, but the agent
/// clients are pointed at a placeholder URI so DI doesn't crash — the app starts up
/// and the user gets a MessageBox via <see cref="FindMissingConfiguration"/>.
/// </remarks>
public sealed class InfrastructureModule : Autofac.Module
{
    public const string FoundryEndpointKey = "FOUNDRY_PROJECT_ENDPOINT";
    private static readonly Uri PlaceholderEndpoint = new("https://foundry-not-configured.invalid/");

    /// <summary>
    /// Inspect <paramref name="config"/> and return any required keys that are missing or blank.
    /// Called from <c>App.OnStartup</c> so the user sees a MessageBox at launch instead of an
    /// opaque crash on first Run click.
    /// </summary>
    public static IReadOnlyList<MissingConfiguration> FindMissingConfiguration(IConfiguration config)
    {
        var missing = new List<MissingConfiguration>();
        if (string.IsNullOrWhiteSpace(config[FoundryEndpointKey]))
        {
            missing.Add(new MissingConfiguration(
                FoundryEndpointKey,
                $"Set via `dotnet user-secrets set {FoundryEndpointKey} https://<your-foundry-project>` in the FikaFinans.Wpf project."));
        }
        return missing;
    }

    /// <summary>
    /// Inspect the persisted AppSettings and return a warning if no model is usable at runtime —
    /// either no deployments exist, the selected model has no matching deployment, or the matched
    /// deployment's name is blank. Returns <c>null</c> when settings are usable.
    /// The runtime falls back to a hardcoded model id so DI never crashes; this check exists to
    /// nudge the user to Settings → Models before the next pipeline run.
    /// </summary>
    public static MissingConfiguration? FindMissingModelConfiguration(AppSettings settings)
    {
        var models = settings.Models;

        if (models.Deployments.Count == 0)
        {
            return new MissingConfiguration(
                "Models.Deployments",
                "No model deployments configured. Open Settings → Models to add at least one.");
        }

        var selected = models.Deployments.FirstOrDefault(d => d.ModelId == models.SelectedModelId);
        if (selected is null)
        {
            return new MissingConfiguration(
                "Models.SelectedModelId",
                $"No deployment matches the selected model '{models.SelectedModelId.Value}'. Open Settings → Models.");
        }

        if (string.IsNullOrWhiteSpace(selected.DeploymentName.Value))
        {
            return new MissingConfiguration(
                $"Models.Deployments[{selected.ModelId.Value}].DeploymentName",
                $"Deployment name for '{selected.ModelId.Value}' is empty. Open Settings → Models.");
        }

        return null;
    }

    protected override void Load(ContainerBuilder builder)
    {
        RegisterSettings(builder);
        RegisterBankServices(builder);
        RegisterFoundryServices(builder);
        RegisterPipelineServices(builder);
    }

    private static void RegisterSettings(ContainerBuilder builder)
    {
        builder.RegisterType<JsonAppSettingsStore>()
            .As<IAppSettingsStore>()
            .SingleInstance();

        builder.RegisterType<WindowsTaskSchedulerWriter>()
            .As<IScheduleWriter>()
            .SingleInstance();
    }

    private static void RegisterBankServices(ContainerBuilder builder)
    {
        // EF Core in-memory DbContext — one shared instance (simulated bank has no concurrency needs)
        builder.Register(_ =>
        {
            var opts = new DbContextOptionsBuilder<BankDbContext>()
                .UseInMemoryDatabase("FikaFinansBankDb")
                .Options;
            return new BankDbContext(opts);
        })
        .AsSelf()
        .SingleInstance();

        // BankSimulator is SingleInstance so the Bank tab and all bank services share one virtual clock.
        builder.RegisterType<BankSimulator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LedgerService>()
            .As<ILedgerService>()
            .SingleInstance();

        builder.RegisterType<TradingService>()
            .As<ITradingService>()
            .SingleInstance();

        builder.RegisterType<PortfolioQueryService>()
            .As<IPortfolioQueryService>()
            .SingleInstance();

        builder.RegisterType<SettlementEngine>()
            .As<ISettlementEngine>()
            .SingleInstance();

        builder.RegisterType<BankCsvImporter>()
            .As<IBankCsvImporter>()
            .SingleInstance();

        builder.RegisterType<DataSeeder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppStartupInitializer>()
            .AsSelf()
            .SingleInstance();
    }

    private static void RegisterFoundryServices(ContainerBuilder builder)
    {
        builder.RegisterType<EmbeddedPromptProvider>()
            .As<IPromptProvider>()
            .SingleInstance();

        builder.RegisterType<DefaultUserPromptProvider>()
            .As<IDefaultUserPromptProvider>()
            .SingleInstance();

        builder.Register(_ => TimeProvider.System)
            .As<TimeProvider>()
            .SingleInstance();

        builder.Register(BuildFundDataFileSet)
            .AsSelf()
            .SingleInstance();

        builder.Register(BuildAzureCredential)
            .As<Azure.Core.TokenCredential>()
            .SingleInstance();

        builder.Register(BuildAIProjectClient)
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FoundryFileStore>()
            .As<IFoundryFileStore>()
            .SingleInstance();

        // One IFundAnalyticsAgent per deployed model. Same adapter, only ModelId differs.
        foreach (var modelId in FoundryModelIds.ComparisonModels)
        {
            var captured = modelId; // avoid foreach-variable capture pitfalls
            builder.Register(ctx => new CodeInterpreterFundAnalyticsAgent(
                    logger: LogManager.GetLogger($"{nameof(CodeInterpreterFundAnalyticsAgent)}.{captured}"),
                    projectClient: ctx.Resolve<AIProjectClient>(),
                    fileStore: ctx.Resolve<IFoundryFileStore>(),
                    promptProvider: ctx.Resolve<IPromptProvider>(),
                    fileSet: ctx.Resolve<FundDataFileSet>(),
                    timeProvider: ctx.Resolve<TimeProvider>(),
                    modelId: captured))
                .As<IFundAnalyticsAgent>()
                .SingleInstance();
        }

        builder.RegisterType<CompareModelsUseCase>()
            .AsSelf()
            .SingleInstance();
    }

    private static FundDataFileSet BuildFundDataFileSet(IComponentContext ctx)
    {
        var settings = ctx.Resolve<IAppSettingsStore>().Load();
        return FundDataFileSet.FromFolder(settings.DataFolder);
    }

    private static DefaultAzureCredential BuildAzureCredential(IComponentContext _)
    {
        return new DefaultAzureCredential();
    }

    private static AIProjectClient BuildAIProjectClient(IComponentContext ctx)
    {
        var endpoint = ResolveFoundryEndpoint(ctx);
        var credential = ctx.Resolve<Azure.Core.TokenCredential>();
        // Reasoning models (gpt-5.x, DeepSeek-R1) running Code Interpreter regularly hold the
        // Responses API socket open past the System.ClientModel default of 100s, then SDK
        // retries swallow the cancellation token. Lift the per-attempt cap so the agent's own
        // RunTimeout governs end-to-end behaviour.
        var options = new AIProjectClientOptions
        {
            NetworkTimeout = TimeSpan.FromMinutes(10),
        };
        return new AIProjectClient(endpoint, credential, options);
    }

    private static Uri ResolveFoundryEndpoint(IComponentContext ctx)
    {
        var config = ctx.Resolve<IConfiguration>();
        var raw = config[FoundryEndpointKey];
        // App.OnStartup already shows a MessageBox listing missing keys via FindMissingConfiguration.
        // Hand the SDK a placeholder so DI graph builds; any actual call surfaces a network error
        // through the per-panel Fail() path instead of crashing startup.
        return string.IsNullOrWhiteSpace(raw) ? PlaceholderEndpoint : new Uri(raw);
    }

    private static void RegisterPipelineServices(ContainerBuilder builder)
    {
        // Paths service — reads AppSettings.Folders at runtime so Settings dialog saves take effect.
        builder.RegisterType<SettingsBackedPathsService>()
            .As<IPathsService>()
            .SingleInstance();

        // Foundry LLM clients used by the hybrid/LLM pipeline steps.
        // All take AIProjectClient (already registered) + the default model ID from settings.
        builder.Register(ctx =>
            new FoundryMacroLlmClient(
                ctx.Resolve<AIProjectClient>(),
                ResolveDefaultModelId(ctx)))
            .As<IMacroLlmClient>()
            .SingleInstance();

        builder.Register(ctx =>
            new FoundryThemeAdjacencyLlmClient(
                ctx.Resolve<AIProjectClient>(),
                ResolveDefaultModelId(ctx)))
            .As<IThemeAdjacencyLlmClient>()
            .SingleInstance();

        builder.Register(ctx =>
            new FoundryFundCatalystLlmClient(
                ctx.Resolve<AIProjectClient>(),
                ResolveDefaultModelId(ctx)))
            .As<IFundCatalystLlmClient>()
            .SingleInstance();

        builder.Register(ctx =>
            new FoundryThesisRefinementLlmClient(
                ctx.Resolve<AIProjectClient>(),
                ResolveDefaultModelId(ctx)))
            .As<IThesisRefinementLlmClient>()
            .SingleInstance();

        builder.Register(ctx =>
            new FoundryDifferentiatorLlmClient(
                ctx.Resolve<AIProjectClient>(),
                ResolveDefaultModelId(ctx)))
            .As<IDifferentiatorLlmClient>()
            .SingleInstance();

        // Pipeline agents — simple ones auto-wire via RegisterType; agents with optional
        // constructor params are registered explicitly to avoid Autofac guessing.
        builder.RegisterType<DataLoaderAgent>().As<IDataLoaderAgent>().SingleInstance();
        builder.RegisterType<MetricsCalculatorAgent>().As<IMetricsCalculatorAgent>().SingleInstance();

        builder.Register(ctx => new MacroAnalystAgent(
                paths: ctx.Resolve<IPathsService>(),
                llm: ctx.Resolve<IMacroLlmClient>()))
            .As<IMacroAnalystAgent>()
            .SingleInstance();

        builder.RegisterType<SignalScorerAgent>().As<ISignalScorerAgent>().SingleInstance();
        builder.RegisterType<MacroAlignerAgent>().As<IMacroAlignerAgent>().SingleInstance();
        builder.RegisterType<CatalystTaggerAgent>().As<ICatalystTaggerAgent>().SingleInstance();
        builder.RegisterType<ThesisValidatorAgent>().As<IThesisValidatorAgent>().SingleInstance();
        builder.RegisterType<RecommenderAgent>().As<IRecommenderAgent>().SingleInstance();
        builder.RegisterType<UniverseEnricherAgent>().As<IUniverseEnricherAgent>().SingleInstance();

        // PortfolioConstructorAgent has two ctors; register explicitly so Autofac
        // uses the (IPathsService) one that defaults to PortfolioConstructorConfig.Default.
        builder.Register(ctx => new PortfolioConstructorAgent(ctx.Resolve<IPathsService>()))
            .As<IPortfolioConstructorAgent>()
            .SingleInstance();
    }

    private static string ResolveDefaultModelId(IComponentContext ctx)
    {
        var models = ctx.Resolve<IAppSettingsStore>().Load().Models;
        var selected = models.Deployments.FirstOrDefault(d => d.ModelId == models.SelectedModelId);
        var name = selected?.DeploymentName.Value;
        return string.IsNullOrWhiteSpace(name) ? FoundryModelIds.Gpt5_4_1 : name;
    }
}
