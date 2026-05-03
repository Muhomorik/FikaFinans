using Autofac;
using Azure.AI.Projects;
using Azure.Identity;
using FikaFinans.Application.Agents;
using FikaFinans.Application.Settings;
using FikaFinans.Application.UseCases;
using FikaFinans.Infrastructure.Foundry;
using FikaFinans.Infrastructure.Prompts;
using FikaFinans.Infrastructure.Settings;
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

    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<JsonAppSettingsStore>()
            .As<IAppSettingsStore>()
            .SingleInstance();

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
}
