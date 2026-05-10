using Autofac;
using FikaFinans.Wpf.ViewModels;
using FikaFinans.Wpf.Views.Dialogs;

namespace FikaFinans.Wpf.Services;

public sealed class FundDetailDialogService : IFundDetailDialogService
{
    private readonly ILifetimeScope _scope;

    public FundDetailDialogService(ILifetimeScope scope)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public void Show(string fundIsin, string isoWeek, string runId, System.Windows.Window? owner = null)
    {
        var vm = _scope.Resolve<FundDetailViewModel>();
        vm.Load(fundIsin, isoWeek, runId);

        var window = _scope.Resolve<FundDetailWindow>();
        window.DataContext = vm;
        if (owner is not null) window.Owner = owner;

        window.Show();
    }
}
