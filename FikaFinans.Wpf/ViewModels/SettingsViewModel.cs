using System.IO;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Agents;
using FikaFinans.Application.Settings;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// Backs the settings gear dialog. Single editable field — the data folder. Apply on
/// restart; we don't try to hot-swap services or rebind the file store.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore? _store;
    private readonly ILogger? _logger;

    private string _dataFolder = string.Empty;
    private string _settingsFilePath = string.Empty;
    private string _foundryFilesPath = string.Empty;
    private string _validationMessage = string.Empty;
    private bool _saved;

    public SettingsViewModel(IAppSettingsStore store, IFoundryFileStore fileStore, ILogger logger) : this()
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(fileStore);

        SettingsFilePath = _store.SettingsFilePath;
        FoundryFilesPath = fileStore.SidecarFilePath;
        DataFolder = _store.Load().DataFolder;
        Validate();
    }

    public SettingsViewModel()
    {
        BrowseCommand = new DelegateCommand(OnBrowse);
        SaveCommand = new DelegateCommand(OnSave, CanSave);
        CancelCommand = new DelegateCommand(OnCancel);
    }

    public string DataFolder
    {
        get => _dataFolder;
        set
        {
            if (SetProperty(ref _dataFolder, value, nameof(DataFolder)))
            {
                Validate();
                ((DelegateCommand)SaveCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string SettingsFilePath
    {
        get => _settingsFilePath;
        private set => SetProperty(ref _settingsFilePath, value, nameof(SettingsFilePath));
    }

    public string FoundryFilesPath
    {
        get => _foundryFilesPath;
        private set => SetProperty(ref _foundryFilesPath, value, nameof(FoundryFilesPath));
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value, nameof(ValidationMessage));
    }

    public bool Saved
    {
        get => _saved;
        private set => SetProperty(ref _saved, value, nameof(Saved));
    }

    public ICommand BrowseCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Raised by the dialog code-behind to close the window once the action completes.</summary>
    public event EventHandler<bool>? CloseRequested;

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        SettingsFilePath = @"C:\Users\<you>\AppData\Roaming\FikaFinans\settings.json";
        FoundryFilesPath = @"C:\Users\<you>\AppData\Roaming\FikaFinans\foundry-files.json";
        DataFolder = @"C:\Path\To\FundData";
        Validate();
    }

    private void OnBrowse()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select fund data folder",
            InitialDirectory = Directory.Exists(DataFolder) ? DataFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };

        if (dialog.ShowDialog() == true)
            DataFolder = dialog.FolderName;
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(DataFolder) && Directory.Exists(DataFolder);

    private void OnSave()
    {
        if (_store is null) return;
        try
        {
            _store.Save(new AppSettings(DataFolder.Trim()));
            Saved = true;
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to save settings");
            ValidationMessage = $"Failed to save: {ex.Message}";
        }
    }

    private void OnCancel() => CloseRequested?.Invoke(this, false);

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(DataFolder))
        {
            ValidationMessage = "Pick a folder.";
            return;
        }
        if (!Directory.Exists(DataFolder))
        {
            ValidationMessage = "Folder does not exist.";
            return;
        }

        var missing = FundDataFiles.All
            .Where(name => !File.Exists(Path.Combine(DataFolder, name)))
            .ToArray();

        ValidationMessage = missing.Length == 0
            ? $"All {FundDataFiles.All.Count} data files present."
            : $"Missing in folder: {string.Join(", ", missing)} (you can still save — drop them in later).";
    }
}
