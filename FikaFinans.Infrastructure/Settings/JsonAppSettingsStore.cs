using System.Text.Json;
using System.Text.Json.Nodes;
using FikaFinans.Application.Settings;
using NLog;

namespace FikaFinans.Infrastructure.Settings;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in
/// <c>%LOCALAPPDATA%\FikaFinans\settings.json</c>.
/// Migrates v1 files (dataFolder + schemaVersion) to the v2 schema on first load.
/// </summary>
public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private const string SettingsFileName = "settings.json";
    private const string AppFolderName = "FikaFinans";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger _logger;
    private readonly string _settingsPath;

    public JsonAppSettingsStore(ILogger logger)
    {
        _logger = logger;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appData, AppFolderName, SettingsFileName);
    }

    public string SettingsFilePath => _settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            var defaults = BuildDefaults();
            _logger.Info("settings.json not found at {Path} — writing first-run defaults", _settingsPath);
            Save(defaults);
            return defaults;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var node = JsonNode.Parse(json);

            // Migrate v1 (dataFolder + schemaVersion=1) → v2
            if (node?["schemaVersion"]?.GetValue<int>() < 2)
            {
                var v1DataFolder = node?["dataFolder"]?.GetValue<string>() ?? string.Empty;
                _logger.Info("Migrating settings.json from v1 to v2 (dataFolder → folders.yieldRaccoonInputs)");
                var migrated = BuildDefaults() with
                {
                    Folders = new FolderSettings { YieldRaccoonInputs = v1DataFolder }
                };
                Save(migrated);
                return migrated;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? BuildDefaults();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to read settings.json at {Path} — falling back to defaults", _settingsPath);
            return BuildDefaults();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var dir = Path.GetDirectoryName(_settingsPath)!;
        Directory.CreateDirectory(dir);
        var tmp = _settingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(tmp, _settingsPath, overwrite: true);
        _logger.Info("Saved settings.json (provider={Provider})", settings.Database.Provider);
    }

    private static AppSettings BuildDefaults() => new();
}
