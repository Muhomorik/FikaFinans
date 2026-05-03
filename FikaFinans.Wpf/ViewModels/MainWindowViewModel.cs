using System.Windows;
using System.Windows.Input;
using Autofac;
using DevExpress.Mvvm;
using FikaFinans.Wpf.Interop;
using FikaFinans.Wpf.Views;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger? _logger;
    private readonly ILifetimeScope? _scope;

    private string _title = string.Empty;
    private CompareModelsViewModel _compareModelsViewModel = new();

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value, nameof(Title));
    }

    public CompareModelsViewModel CompareModelsViewModel
    {
        get => _compareModelsViewModel;
        private set => SetProperty(ref _compareModelsViewModel, value, nameof(CompareModelsViewModel));
    }

    public ICommand OpenSettingsCommand { get; }

    /// <summary>Runtime constructor (DI).</summary>
    public MainWindowViewModel(ILogger logger, ILifetimeScope scope, CompareModelsViewModel compareModelsViewModel) : this()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
        CompareModelsViewModel = compareModelsViewModel ?? throw new ArgumentNullException(nameof(compareModelsViewModel));
        _logger.Info("MainWindowViewModel initialized");
    }

    /// <summary>Design-time constructor — required for <c>d:DataContext IsDesignTimeCreatable=True</c>.</summary>
    public MainWindowViewModel()
    {
        OpenSettingsCommand = new DelegateCommand(OnOpenSettings);
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        Title = "FikaFinans (Design)";
    }

    protected override void OnInitializeInRuntime()
    {
        base.OnInitializeInRuntime();
        Title = "FikaFinans";
    }

    private void OnOpenSettings()
    {
        if (_scope is null) return;
        try
        {
            var dialog = _scope.Resolve<SettingsWindow>();
            dialog.Owner = System.Windows.Application.Current.MainWindow;
            var result = dialog.ShowDialog();
            if (result == true)
            {
                TaskDialog.ShowInformation(
                    owner: System.Windows.Application.Current.MainWindow,
                    title: "FikaFinans",
                    mainInstruction: "Settings saved",
                    content: "Restart FikaFinans for the new data folder to take effect.");
            }
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to open settings dialog");
        }
    }
}
