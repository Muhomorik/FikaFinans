using FikaFinans.Domain.Models;

namespace FikaFinans.Application.Settings;

/// <summary>
/// Persistent app settings v2. Stored as JSON in <c>%LOCALAPPDATA%\FikaFinans\settings.json</c>.
/// v1 files (schemaVersion &lt; 2) are migrated on first load by JsonAppSettingsStore.
/// </summary>
public sealed record AppSettings
{
    public int SchemaVersion { get; init; } = 2;
    public DatabaseSettings Database { get; init; } = new();
    public ModelsSettings Models { get; init; } = new();
    public FolderSettings Folders { get; init; } = new();
    public ScheduleSettings Schedules { get; init; } = new();
    public SyncSettings Sync { get; init; } = new();

    /// <summary>Backward-compat accessor — maps to <see cref="FolderSettings.YieldRaccoonInputs"/>.</summary>
    public string DataFolder => Folders.YieldRaccoonInputs;
}

public sealed record DatabaseSettings
{
    public string Provider { get; init; } = "InMemory";
    public string Path { get; init; } = string.Empty;
    public string BackendApiUrl { get; init; } = string.Empty;
    public string BackendApiKey { get; init; } = string.Empty;
}

public sealed record ModelsSettings
{
    public List<ModelDeployment> Deployments { get; init; } = [];
    public ModelFamilyId SelectedModelId { get; init; } = new(string.Empty);
    public string FoundryEndpoint { get; init; } = string.Empty;
    public string FoundryApiKey { get; init; } = string.Empty;
    public string BingGroundingKey { get; init; } = string.Empty;
}

/// <summary>Pairs a user-facing model family with the Azure deployment string used to call Foundry.</summary>
public sealed record ModelDeployment(ModelFamilyId ModelId, FoundryDeploymentName DeploymentName);

public sealed record FolderSettings
{
    public string YieldRaccoonInputs { get; init; } = string.Empty;
    public string AnalyticsJson { get; init; } = string.Empty;
    public string StepOutputs { get; init; } = string.Empty;
    public string Examples { get; init; } = string.Empty;
}

public sealed record ScheduleSettings
{
    public DailyAutoRunSettings DailyAutoRun { get; init; } = new();
    public WeeklyExportSettings WeeklyExport { get; init; } = new();
}

public sealed record DailyAutoRunSettings
{
    public bool Enabled { get; init; }
    public string Time { get; init; } = "20:00";
    public bool PassAutoList { get; init; }
}

public sealed record WeeklyExportSettings
{
    public bool Enabled { get; init; }
    public string DayOfWeek { get; init; } = "Thursday";
    public string Time { get; init; } = "22:00";
    public string LastRunAt { get; init; } = string.Empty;
    public int LastRunRowCount { get; init; }
}

public sealed record SyncSettings
{
    public string BaseUrl { get; init; } = string.Empty;
    public string AuthToken { get; init; } = string.Empty;
}
