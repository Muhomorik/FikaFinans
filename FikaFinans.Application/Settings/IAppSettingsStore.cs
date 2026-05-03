namespace FikaFinans.Application.Settings;

/// <summary>
/// Port for loading/saving <see cref="AppSettings"/>. Apply-on-restart semantics —
/// no hot-reload, no <c>IOptionsMonitor</c>. Call <see cref="Load"/> once at startup;
/// <see cref="Save"/> writes the JSON and the user is told to restart.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>Absolute path to the on-disk settings file (exposed so the UI can show it).</summary>
    string SettingsFilePath { get; }

    /// <summary>Returns the current settings, creating the file with first-run defaults if absent.</summary>
    AppSettings Load();

    /// <summary>Persists settings. Caller is responsible for prompting the user to restart.</summary>
    void Save(AppSettings settings);
}
