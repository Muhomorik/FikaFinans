namespace FikaFinans.Application.Settings;

/// <summary>
/// Persistent app settings. Stored as JSON in <c>%APPDATA%\FikaFinans\settings.json</c>.
/// </summary>
public sealed record AppSettings(string DataFolder)
{
    /// <summary>JSON sidecar schema version — bump when fields are added/removed.</summary>
    public int SchemaVersion { get; init; } = 1;
}
