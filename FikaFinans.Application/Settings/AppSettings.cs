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
    public PipelineSettings Pipeline { get; init; } = new();

    /// <summary>Backward-compat accessor — maps to <see cref="FolderSettings.YieldRaccoonInputs"/>.</summary>
    public string DataFolder => Folders.YieldRaccoonInputs;
}

public sealed record PipelineSettings
{
    /// <summary>
    /// Concurrency cap for the streaming runner's per-ISIN block. Lifted out
    /// of the hardcoded Slice 4 default on 2026-05-27 per Open Question #5 in
    /// <c>Docs/pipeline-step-flow-plan.md</c>. Lower values reduce per-fund
    /// Foundry pressure at the cost of wall-clock; higher values do the
    /// reverse.
    /// </summary>
    public int MaxConcurrentFunds { get; init; } = 5;

    /// <summary>
    /// Developer-debugging only — leave <c>false</c> in normal use.
    /// Default flipped from <c>true</c> to <c>false</c> on 2026-05-30
    /// (Phase 8 sub-step 8d in
    /// <c>Docs/storage-migration-plan.md</c>). When <c>true</c>, the
    /// streaming runner writes per-ISIN boundary JSON files to disk in
    /// addition to populating the SQLite IsinProgress columns. The
    /// canonical source is the SQLite columns; WPF reads from there.
    /// Flip to <c>true</c> for diff-against-prior-run debugging or
    /// ad-hoc CLI tooling.
    /// </summary>
    public bool WriteDiskJsonArtifacts { get; init; } = false;
}

public sealed record DatabaseSettings
{
    /// <summary>
    /// Which storage backend to use: <c>"Sqlite"</c> (default, on-disk file),
    /// <c>"InMemory"</c> (transient — loses state on exit; tests use this),
    /// or <c>"AzureTables"</c> (Phase 6, not yet implemented).
    /// </summary>
    public string Provider { get; init; } = "Sqlite";

    /// <summary>
    /// Optional override for the SQLite file path. When blank, defaults to
    /// <c>%USERPROFILE%\Documents\FikaFinans\fikafinans.db</c>.
    /// </summary>
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
