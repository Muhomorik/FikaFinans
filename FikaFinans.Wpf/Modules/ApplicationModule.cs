using Autofac;
using FikaFinans.Wpf.Services;

namespace FikaFinans.Wpf.Modules;

public class ApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ConfigEditorDialogService>()
            .As<IConfigEditorDialogService>()
            .SingleInstance();

        builder.RegisterType<FundDetailDialogService>()
            .As<IFundDetailDialogService>()
            .SingleInstance();
    }
}
