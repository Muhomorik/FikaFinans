using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using DevExpress.Mvvm;
using FikaFinans.Application.Schedules;
using FikaFinans.Application.Settings;
using FikaFinans.Domain.Models;
using NLog;

namespace FikaFinans.Wpf.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsStore? _store;
    private readonly IScheduleWriter? _scheduleWriter;
    private readonly ILogger? _logger;

    // ── Database tab ──────────────────────────────────────────────
    private string _dbProvider = "InMemory";
    private string _dbPath = string.Empty;
    private string _backendApiUrl = string.Empty;
    private string _backendApiKey = string.Empty;

    // ── Models tab ────────────────────────────────────────────────
    private string? _selectedModelId;
    private string _foundryEndpoint = string.Empty;
    private string _foundryApiKey = string.Empty;
    private string _bingGroundingKey = string.Empty;

    // ── Folders tab ───────────────────────────────────────────────
    private string _yieldRaccoonInputs = string.Empty;
    private string _analyticsJson = string.Empty;
    private string _stepOutputs = string.Empty;
    private string _examples = string.Empty;

    // ── Schedules tab ─────────────────────────────────────────────
    private bool _dailyAutoRunEnabled;
    private DateTime _dailyAutoRunTime = DateTime.Today.AddHours(20);
    private bool _dailyPassAutoList;
    private bool _weeklyExportEnabled;
    private string _weeklyExportDayOfWeek = "Thursday";
    private DateTime _weeklyExportTime = DateTime.Today.AddHours(22);

    // ── Sync tab ──────────────────────────────────────────────────
    private string _syncBaseUrl = string.Empty;
    private string _syncAuthToken = string.Empty;

    private string _settingsFilePath = string.Empty;

    #region Properties

    public string DbProvider { get => _dbProvider; set => SetProperty(ref _dbProvider, value, nameof(DbProvider)); }
    public string DbPath { get => _dbPath; set => SetProperty(ref _dbPath, value, nameof(DbPath)); }
    public string BackendApiUrl { get => _backendApiUrl; set => SetProperty(ref _backendApiUrl, value, nameof(BackendApiUrl)); }
    public string BackendApiKey { get => _backendApiKey; set => SetProperty(ref _backendApiKey, value, nameof(BackendApiKey)); }

    public ObservableCollection<ModelDeploymentEntryViewModel> Deployments { get; } = new();

    public string? SelectedModelId { get => _selectedModelId; set => SetProperty(ref _selectedModelId, value, nameof(SelectedModelId)); }

    public string FoundryEndpoint { get => _foundryEndpoint; set => SetProperty(ref _foundryEndpoint, value, nameof(FoundryEndpoint)); }
    public string FoundryApiKey { get => _foundryApiKey; set => SetProperty(ref _foundryApiKey, value, nameof(FoundryApiKey)); }
    public string BingGroundingKey { get => _bingGroundingKey; set => SetProperty(ref _bingGroundingKey, value, nameof(BingGroundingKey)); }

    public string YieldRaccoonInputs { get => _yieldRaccoonInputs; set => SetProperty(ref _yieldRaccoonInputs, value, nameof(YieldRaccoonInputs)); }
    public string AnalyticsJson { get => _analyticsJson; set => SetProperty(ref _analyticsJson, value, nameof(AnalyticsJson)); }
    public string StepOutputs { get => _stepOutputs; set => SetProperty(ref _stepOutputs, value, nameof(StepOutputs)); }
    public string Examples { get => _examples; set => SetProperty(ref _examples, value, nameof(Examples)); }

    public bool DailyAutoRunEnabled { get => _dailyAutoRunEnabled; set => SetProperty(ref _dailyAutoRunEnabled, value, nameof(DailyAutoRunEnabled)); }
    public DateTime DailyAutoRunTime { get => _dailyAutoRunTime; set => SetProperty(ref _dailyAutoRunTime, value, nameof(DailyAutoRunTime)); }
    public bool DailyPassAutoList { get => _dailyPassAutoList; set => SetProperty(ref _dailyPassAutoList, value, nameof(DailyPassAutoList)); }
    public bool WeeklyExportEnabled { get => _weeklyExportEnabled; set => SetProperty(ref _weeklyExportEnabled, value, nameof(WeeklyExportEnabled)); }
    public string WeeklyExportDayOfWeek { get => _weeklyExportDayOfWeek; set => SetProperty(ref _weeklyExportDayOfWeek, value, nameof(WeeklyExportDayOfWeek)); }
    public DateTime WeeklyExportTime { get => _weeklyExportTime; set => SetProperty(ref _weeklyExportTime, value, nameof(WeeklyExportTime)); }

    public string SyncBaseUrl { get => _syncBaseUrl; set => SetProperty(ref _syncBaseUrl, value, nameof(SyncBaseUrl)); }
    public string SyncAuthToken { get => _syncAuthToken; set => SetProperty(ref _syncAuthToken, value, nameof(SyncAuthToken)); }

    public string SettingsFilePath { get => _settingsFilePath; private set => SetProperty(ref _settingsFilePath, value, nameof(SettingsFilePath)); }

    public List<string> DbProviders { get; } = ["InMemory", "SQLite", "DualWrite"];
    public List<string> DaysOfWeek { get; } = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    #endregion

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand WindowClosingCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand BrowseInputsCommand { get; }
    public ICommand BrowseOutputsCommand { get; }
    public ICommand BrowseDatabaseLocationCommand { get; }
    public ICommand BrowseAnalyticsJsonCommand { get; }
    public ICommand BrowseExamplesCommand { get; }
    public ICommand OpenSettingsLocationCommand { get; }
    public ICommand AddModelCommand { get; }
    public ICommand RemoveModelCommand { get; }

    public SettingsViewModel(IAppSettingsStore store, IScheduleWriter scheduleWriter, ILogger logger) : this()
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _scheduleWriter = scheduleWriter ?? throw new ArgumentNullException(nameof(scheduleWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SettingsFilePath = store.SettingsFilePath;
        LoadFromStore();
    }

    public SettingsViewModel()
    {
        SaveCommand = new DelegateCommand(OnSave);
        CancelCommand = new DelegateCommand(OnCancel);
        WindowClosingCommand = new DelegateCommand(OnWindowClosing);
        ResetCommand = new DelegateCommand(OnReset);
        BrowseInputsCommand = new DelegateCommand(() => BrowseFolder(v => YieldRaccoonInputs = v));
        BrowseOutputsCommand = new DelegateCommand(() => BrowseFolder(v => StepOutputs = v));
        BrowseDatabaseLocationCommand = new DelegateCommand(() => BrowseDatabaseFile(v => DbPath = v, DbPath));
        BrowseAnalyticsJsonCommand = new DelegateCommand(() => BrowseFolder(v => AnalyticsJson = v));
        BrowseExamplesCommand = new DelegateCommand(() => BrowseFolder(v => Examples = v));
        OpenSettingsLocationCommand = new DelegateCommand(OnOpenSettingsLocation);
        AddModelCommand = new DelegateCommand(OnAddModel);
        RemoveModelCommand = new DelegateCommand<ModelDeploymentEntryViewModel>(OnRemoveModel);
    }

    private void OnAddModel()
    {
        Deployments.Add(new ModelDeploymentEntryViewModel(string.Empty, string.Empty));
    }

    private void OnRemoveModel(ModelDeploymentEntryViewModel? entry)
    {
        if (entry is null) return;
        Deployments.Remove(entry);
        if (Deployments.All(d => d.ModelId != SelectedModelId))
            SelectedModelId = Deployments.FirstOrDefault()?.ModelId;
    }

    protected override void OnInitializeInDesignMode()
    {
        base.OnInitializeInDesignMode();
        DbProvider = "InMemory";
        Deployments.Clear();
        Deployments.Add(new ModelDeploymentEntryViewModel("gpt-5.4", "gpt-5.4-1"));
        Deployments.Add(new ModelDeploymentEntryViewModel("DeepSeek-R1-0528", "DeepSeek-R1-0528-1"));
        SelectedModelId = "gpt-5.4";
        SettingsFilePath = @"%LOCALAPPDATA%\FikaFinans\settings.json";
        YieldRaccoonInputs = @"C:\Repos\FikaFinans\FikaFinans.InfrastructureV2.Tests\docs\inputs";
    }

    private void LoadFromStore()
    {
        if (_store is null) return;
        var s = _store.Load();

        DbProvider = s.Database.Provider;
        DbPath = s.Database.Path;
        BackendApiUrl = s.Database.BackendApiUrl;
        BackendApiKey = s.Database.BackendApiKey;

        Deployments.Clear();
        foreach (var d in s.Models.Deployments)
            Deployments.Add(new ModelDeploymentEntryViewModel(d.ModelId.Value, d.DeploymentName.Value));
        SelectedModelId = Deployments.Any(d => d.ModelId == s.Models.SelectedModelId.Value)
            ? s.Models.SelectedModelId.Value
            : Deployments.FirstOrDefault()?.ModelId;
        FoundryEndpoint = s.Models.FoundryEndpoint;
        FoundryApiKey = s.Models.FoundryApiKey;
        BingGroundingKey = s.Models.BingGroundingKey;

        YieldRaccoonInputs = s.Folders.YieldRaccoonInputs;
        AnalyticsJson = s.Folders.AnalyticsJson;
        StepOutputs = s.Folders.StepOutputs;
        Examples = s.Folders.Examples;

        DailyAutoRunEnabled = s.Schedules.DailyAutoRun.Enabled;
        DailyAutoRunTime = ParseTimeOfDay(s.Schedules.DailyAutoRun.Time);
        DailyPassAutoList = s.Schedules.DailyAutoRun.PassAutoList;
        WeeklyExportEnabled = s.Schedules.WeeklyExport.Enabled;
        WeeklyExportDayOfWeek = s.Schedules.WeeklyExport.DayOfWeek;
        WeeklyExportTime = ParseTimeOfDay(s.Schedules.WeeklyExport.Time);

        SyncBaseUrl = s.Sync.BaseUrl;
        SyncAuthToken = s.Sync.AuthToken;
    }

    private void OnSave()
    {
        if (_store is null) return;
        try
        {
            var settings = BuildSettings();
            _store.Save(settings);
            _scheduleWriter?.ApplySchedules(settings.Schedules);
            _logger?.Info("Settings saved");
            GetService<ICurrentWindowService>()?.Close();
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Failed to save settings");
        }
    }

    // Cancel button: requests the window to close (triggers OnWindowClosing via ClosingCommand).
    private void OnCancel() => GetService<ICurrentWindowService>()?.Close();

    // ClosingCommand: called by CurrentWindowService when the window is already closing.
    // Must NOT call Close() again — that causes "cannot close while closing" exception.
    private void OnWindowClosing() { /* discard unsaved edits — nothing to do */ }

    private void OnReset()
    {
        LoadFromStore();
    }

    private AppSettings BuildSettings() => new()
    {
        Database = new DatabaseSettings
        {
            Provider = DbProvider,
            Path = DbPath,
            BackendApiUrl = BackendApiUrl,
            BackendApiKey = BackendApiKey
        },
        Models = new ModelsSettings
        {
            Deployments = Deployments
                .Select(d => new ModelDeployment(new ModelFamilyId(d.ModelId), new FoundryDeploymentName(d.DeploymentName)))
                .ToList(),
            SelectedModelId = new ModelFamilyId(SelectedModelId ?? string.Empty),
            FoundryEndpoint = FoundryEndpoint,
            FoundryApiKey = FoundryApiKey,
            BingGroundingKey = BingGroundingKey
        },
        Folders = new FolderSettings
        {
            YieldRaccoonInputs = YieldRaccoonInputs,
            AnalyticsJson = AnalyticsJson,
            StepOutputs = StepOutputs,
            Examples = Examples
        },
        Schedules = new ScheduleSettings
        {
            DailyAutoRun = new DailyAutoRunSettings
            {
                Enabled = DailyAutoRunEnabled,
                Time = FormatTimeOfDay(DailyAutoRunTime),
                PassAutoList = DailyPassAutoList
            },
            WeeklyExport = new WeeklyExportSettings
            {
                Enabled = WeeklyExportEnabled,
                DayOfWeek = WeeklyExportDayOfWeek,
                Time = FormatTimeOfDay(WeeklyExportTime)
            }
        },
        Sync = new SyncSettings
        {
            BaseUrl = SyncBaseUrl,
            AuthToken = SyncAuthToken
        }
    };

    private static void BrowseFolder(Action<string> setter)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select folder" };
        if (dialog.ShowDialog() == true)
            setter(dialog.FolderName);
    }

    private static DateTime ParseTimeOfDay(string s) =>
        TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts)
            ? DateTime.Today.Add(ts)
            : DateTime.Today;

    private static string FormatTimeOfDay(DateTime dt) => $"{dt.Hour:D2}:{dt.Minute:D2}";

    private static void BrowseDatabaseFile(Action<string> setter, string current)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Database location",
            Filter = "SQLite database (*.db;*.sqlite)|*.db;*.sqlite|All files (*.*)|*.*",
            DefaultExt = ".db",
            OverwritePrompt = false,
            CheckPathExists = false,
            FileName = string.IsNullOrWhiteSpace(current) ? "fikafinans.db" : current
        };
        if (dialog.ShowDialog() == true)
            setter(dialog.FileName);
    }

    private void OnOpenSettingsLocation()
    {
        var path = SettingsFilePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start("explorer.exe", $"\"{dir}\"");
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn(ex, "Failed to open settings file location");
        }
    }
}
