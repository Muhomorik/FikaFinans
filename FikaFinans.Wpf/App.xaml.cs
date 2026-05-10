using System.IO;
using System.Windows;
using System.Windows.Media;
using Autofac;
using ControlzEx.Theming;
using FikaFinans.Application.Settings;
using FikaFinans.Infrastructure;
using FikaFinans.Infrastructure.Bank.Persistence;
using FikaFinans.Infrastructure.DependencyInjection;
using FikaFinans.Wpf.Interop;
using FikaFinans.Wpf.Modules;
using Microsoft.Extensions.Configuration;
using NLog;

namespace FikaFinans.Wpf;

/// <summary>
/// Application entry point. Builds Autofac container from modules and creates the main window.
/// </summary>
public partial class App : System.Windows.Application
{
    private IContainer? _container;
    private ILifetimeScope? _appScope;
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LogManager.Setup().LoadConfigurationFromFile("NLog.config");
        Logger.Info("Application starting...");

        ApplyFikaFinansTheme();

        EnsureDefaultDirectories();

        WireUpExceptionHandlers();

        var configuration = BuildConfiguration();

        WarnIfConfigurationIncomplete(configuration);

        var builder = new ContainerBuilder();
        builder.RegisterInstance(configuration).As<IConfiguration>();
        builder.RegisterModule<NLogModule>();
        builder.RegisterModule<ApplicationModule>();
        builder.RegisterModule<InfrastructureModule>();
        builder.RegisterModule(new PresentationModule(configuration));

        _container = builder.Build();
        _appScope = _container.BeginLifetimeScope();

        Logger.Info("DI container configured");

        WarnIfModelsNotConfigured(_appScope.Resolve<IAppSettingsStore>());

        // Create directories and default config files before any tab or agent needs them.
        _appScope.Resolve<AppStartupInitializer>().Initialize();

        // Seed the bank's chart of accounts before any tab can post an order.
        _appScope.Resolve<DataSeeder>().SeedAsync().GetAwaiter().GetResult();

        var mainWindow = _appScope.Resolve<MainWindow>();
        mainWindow.Show();

        Logger.Info("Application started successfully");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("Application exiting...");

        _appScope?.Dispose();
        _container?.Dispose();

        LogManager.Shutdown();

        base.OnExit(e);
    }

    private void WireUpExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error(args.Exception, "Unhandled UI exception");
            MessageBox.Show(
                $"An unexpected error occurred:\n\n{args.Exception.Message}",
                "FikaFinans Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Logger.Fatal(ex, "Unhandled domain exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddUserSecrets<App>(optional: true)
            .Build();
    }

    /// <summary>
    /// Applies the MahApps theme derived from the Windows system accent color.
    /// Light.Blue.xaml in App.xaml serves as a XAML designer fallback only.
    /// </summary>
    private static void ApplyFikaFinansTheme()
    {
        try
        {
            var accentColor = SystemParameters.WindowGlassColor;

            // Fall back to Windows 11 default blue if transparent or black
            if (accentColor.A == 0 || (accentColor.R == 0 && accentColor.G == 0 && accentColor.B == 0))
                accentColor = (Color)ColorConverter.ConvertFromString("#0078D4")!;

            var theme = RuntimeThemeGenerator.Current
                .GenerateRuntimeTheme("Light", accentColor);

            if (theme is null)
            {
                Logger.Warn("RuntimeThemeGenerator returned null, falling back to Light.Blue");
                return;
            }

            ThemeManager.Current.ChangeTheme(System.Windows.Application.Current, theme);
            Logger.Info("Applied FikaFinans theme with accent #{R:X2}{G:X2}{B:X2}",
                accentColor.R, accentColor.G, accentColor.B);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to apply FikaFinans theme, falling back to Light.Blue");
        }
    }

    private static void EnsureDefaultDirectories()
    {
        var appName  = typeof(App).Namespace!.Split('.')[0];
        var docsBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), appName);

        Directory.CreateDirectory(Path.Combine(docsBase, "inputs"));
        Directory.CreateDirectory(Path.Combine(docsBase, "stepOutputs"));
    }

    private static void WarnIfConfigurationIncomplete(IConfiguration configuration)
    {
        var missing = InfrastructureModule.FindMissingConfiguration(configuration);
        if (missing.Count == 0) return;

        Logger.Warn("Startup configuration incomplete: {Keys}", string.Join(", ", missing.Select(m => m.Key)));

        var lines = missing.Select(m => $"• {m.Key}\n   {m.Hint}");
        var body =
            string.Join("\n\n", lines) +
            "\n\nThe app will start, but LLM pipeline steps (03, 06, 07, 09) will fail until these are set.";

        TaskDialog.ShowWarning(
            owner: null,
            title: "FikaFinans",
            mainInstruction: "Configuration is missing",
            content: body);
    }

    private static void WarnIfModelsNotConfigured(IAppSettingsStore store)
    {
        var missing = InfrastructureModule.FindMissingModelConfiguration(store.Load());
        if (missing is null) return;

        Logger.Warn("Model configuration incomplete: {Key} — {Hint}", missing.Key, missing.Hint);

        TaskDialog.ShowWarning(
            owner: null,
            title: "FikaFinans",
            mainInstruction: "No model is configured",
            content: $"{missing.Hint}\n\nThe app will start, but pipeline runs will fail until a model and its Foundry deployment name are saved.");
    }
}
