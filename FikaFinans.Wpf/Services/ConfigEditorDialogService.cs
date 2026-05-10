using Autofac;
using FikaFinans.Wpf.ViewModels;
using FikaFinans.Wpf.Views.Dialogs;

namespace FikaFinans.Wpf.Services;

public sealed class ConfigEditorDialogService : IConfigEditorDialogService
{
    private readonly ILifetimeScope _scope;

    public ConfigEditorDialogService(ILifetimeScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public bool Edit(string configFilePath, System.Windows.Window? owner = null)
    {
        var vm = _scope.Resolve<ConfigEditorViewModel>();
        vm.LoadFile(configFilePath);

        var window = _scope.Resolve<ConfigEditorWindow>();
        window.DataContext = vm;
        if (owner is not null) window.Owner = owner;

        window.ShowDialog();
        return vm.SaveRequested;
    }
}
