using System.Windows.Controls;

namespace FikaFinans.Wpf.Views.NavSync;

/// <summary>
/// The NAV Sync tab view — local simulation of the Azure Queue Storage front
/// door. Hosts the company-filtered status grid + Refresh / Check-and-run
/// buttons; bound to <see cref="ViewModels.NavSyncViewModel"/>.
/// </summary>
public partial class NavSyncView : UserControl
{
    public NavSyncView()
    {
        InitializeComponent();
    }
}
