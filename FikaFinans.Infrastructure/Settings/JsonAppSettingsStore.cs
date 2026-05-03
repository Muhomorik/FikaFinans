using System.Text.Json;
using FikaFinans.Application.Settings;
using NLog;

namespace FikaFinans.Infrastructure.Settings;

/// <summary>
/// Loads and persists <see cref="AppSettings"/> as JSON in
/// <c>%APPDATA%\FikaFinans\settings.json</c>. First run defaults <c>DataFolder</c> to
/// <see cref="AppContext.BaseDirectory"/>'s <c>Docs\</c> sibling — the user is expected
/// to point this at their real export location via the gear icon.
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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null || string.IsNullOrWhiteSpace(loaded.DataFolder))
            {
                _logger.Warn("settings.json at {Path} parsed to null/empty — falling back to defaults", _settingsPath);
                return BuildDefaults();
            }
            return loaded;
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
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
        _logger.Info("Saved settings.json (DataFolder={DataFolder})", settings.DataFolder);
    }

    private static AppSettings BuildDefaults()
    {
        var fallback = Path.Combine(AppContext.BaseDirectory, "Docs");
        return new AppSettings(fallback);
    }
}
