using System.Windows;
using FikaFinans.Wpf.ViewModels;
using MahApps.Metro.Controls;

namespace FikaFinans.Wpf.Views;

public partial class SettingsWindow : MetroWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
    }

    /// <summary>True when the user saved (i.e. they should be told to restart).</summary>
    public bool Saved { get; private set; }

    private void OnCloseRequested(object? sender, bool saved)
    {
        Saved = saved;
        DialogResult = saved;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.CloseRequested -= OnCloseRequested;
        base.OnClosed(e);
    }
}
