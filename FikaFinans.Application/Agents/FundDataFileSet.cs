namespace FikaFinans.Application.Agents;

/// <summary>
/// Resolved absolute paths for the canonical data files in the active data folder.
/// Built once at startup from <see cref="Settings.AppSettings.DataFolder"/>.
/// </summary>
public sealed record FundDataFileSet(
    string SummaryPath,
    string MetadataPath,
    string PositionsPath,
    string StructurePath,
    string AnalyticsRotationTargetsPath,
    string AnalyticsSubstitutionChainPath,
    string AnalyticsWeeklySummaryPath)
{
    /// <summary>Resolve all canonical paths against the given folder.</summary>
    public static FundDataFileSet FromFolder(string dataFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolder);
        return new FundDataFileSet(
            SummaryPath: Path.Combine(dataFolder, FundDataFiles.Summary),
            MetadataPath: Path.Combine(dataFolder, FundDataFiles.Metadata),
            PositionsPath: Path.Combine(dataFolder, FundDataFiles.Positions),
            StructurePath: Path.Combine(dataFolder, FundDataFiles.Structure),
            AnalyticsRotationTargetsPath: Path.Combine(dataFolder, FundDataFiles.AnalyticsRotationTargets),
            AnalyticsSubstitutionChainPath: Path.Combine(dataFolder, FundDataFiles.AnalyticsSubstitutionChain),
            AnalyticsWeeklySummaryPath: Path.Combine(dataFolder, FundDataFiles.AnalyticsWeeklySummary));
    }

    /// <summary>(logicalName, absolutePath) pairs in canonical order.</summary>
    public IEnumerable<(string LogicalName, string LocalPath)> EnumerateFiles()
    {
        yield return (FundDataFiles.Summary, SummaryPath);
        yield return (FundDataFiles.Metadata, MetadataPath);
        yield return (FundDataFiles.Positions, PositionsPath);
        yield return (FundDataFiles.Structure, StructurePath);
        yield return (FundDataFiles.AnalyticsRotationTargets, AnalyticsRotationTargetsPath);
        yield return (FundDataFiles.AnalyticsSubstitutionChain, AnalyticsSubstitutionChainPath);
        yield return (FundDataFiles.AnalyticsWeeklySummary, AnalyticsWeeklySummaryPath);
    }
}
