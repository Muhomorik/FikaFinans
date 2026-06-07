using System.Text.Json;
using FikaFinans.Application.Settings;

namespace FikaFinans.Application.Tests.Settings;

[TestFixture]
public sealed class NavSyncSettingsTests
{
    // Mirrors the options JsonAppSettingsStore persists with.
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Test]
    public void AppSettings_NavSyncSection_RoundTripsThroughCamelCaseJson()
    {
        var original = new AppSettings
        {
            NavSync = new NavSyncSettings
            {
                YieldRaccoonDbPath = @"C:\yr\YieldRaccoon.db",
                CompanyFilter = "Schroder",
            },
        };

        var json = JsonSerializer.Serialize(original, Options);
        var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("navSync"), "section serializes as camelCase");
            Assert.That(loaded.NavSync.YieldRaccoonDbPath, Is.EqualTo(@"C:\yr\YieldRaccoon.db"));
            Assert.That(loaded.NavSync.CompanyFilter, Is.EqualTo("Schroder"));
        });
    }

    [Test]
    public void AppSettings_NavSync_DefaultsToEmpty()
    {
        var settings = new AppSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.NavSync.YieldRaccoonDbPath, Is.Empty);
            Assert.That(settings.NavSync.CompanyFilter, Is.Empty);
        });
    }
}
