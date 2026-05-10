using FikaFinans.Wpf.ViewModels;
using MahApps.Metro.Controls;

namespace FikaFinans.Wpf.Views;

public partial class SettingsWindow : MetroWindow
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
