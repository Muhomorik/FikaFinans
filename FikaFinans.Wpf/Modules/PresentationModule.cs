using System.Reactive.Concurrency;
using System.Reflection;
using System.Windows.Threading;
using Autofac;
using Microsoft.Extensions.Configuration;

namespace FikaFinans.Wpf.Modules;

/// <summary>
/// Registers Presentation layer components: ViewModels, Views, and MainWindow.
/// ViewModels and Views are auto-discovered by name suffix.
/// </summary>
public class PresentationModule : Autofac.Module
{
    private readonly IConfiguration _configuration;

    public PresentationModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        RegisterScheduler(builder);
        RegisterViewModels(builder);
        RegisterViews(builder);
    }

    private static void RegisterScheduler(ContainerBuilder builder)
    {
        // DispatcherScheduler wraps the WPF UI dispatcher so Rx pipelines can
        // ObserveOn(_uiScheduler) to marshal updates back to the UI thread.
        builder.Register(_ => new DispatcherScheduler(Dispatcher.CurrentDispatcher))
            .As<IScheduler>()
            .SingleInstance();
    }

    private static void RegisterViewModels(ContainerBuilder builder)
    {
        var assembly = Assembly.GetExecutingAssembly();

        builder.RegisterAssemblyTypes(assembly)
            .Where(t => t.Name.EndsWith("ViewModel"))
            .AsSelf()
            .InstancePerDependency();
    }

    private static void RegisterViews(ContainerBuilder builder)
    {
        builder.RegisterType<MainWindow>().AsSelf().InstancePerDependency();

        var assembly = Assembly.GetExecutingAssembly();

        builder.RegisterAssemblyTypes(assembly)
            .Where(t => t != typeof(MainWindow)
                        && (t.Name.EndsWith("View") || t.Name.EndsWith("Window")))
            .AsSelf()
            .InstancePerDependency();
    }
}
