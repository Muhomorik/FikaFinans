using FikaFinans.Wpf.ViewModels;
using MahApps.Metro.Controls;

namespace FikaFinans.Wpf;

public partial class MainWindow : MetroWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
