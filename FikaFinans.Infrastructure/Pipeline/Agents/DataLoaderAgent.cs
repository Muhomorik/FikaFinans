using FikaFinans.Application.Paths;
using FikaFinans.Application.Pipeline.Agents;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Infrastructure.Pipeline.Csv;
using FikaFinans.Infrastructure.Pipeline.Json;
using System.Text.Json;

namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class DataLoaderAgent : IDataLoaderAgent
{
    private readonly IPathsService _paths;
    private readonly MetadataCsvParser _metadataParser = new();
    private readonly SummaryCsvParser _summaryParser = new();
    private readonly SnapshotCsvParser _snapshotParser = new();
    private readonly PositionsCsvParser _positionsParser = new();
    private readonly PortfolioStructureMdParser _portfolioParser = new();

    public DataLoaderAgent(IPathsService paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
    }

    public DataLoaderOutput Run(string family, string isoWeek, string runId)
    {
        var metadataPath  = _paths.MetadataCsv(family, isoWeek);
        var summaryPath   = _paths.SummaryCsv(family, isoWeek);
        var snapshotPath  = _paths.SnapshotCsv(family, isoWeek);
        var positionsPath = _paths.PositionsCsv;
        var structurePath = _paths.PortfolioStructureMd;

        try
        {
            VerifyFilenameIsoWeekTags(isoWeek, metadataPath, summaryPath, snapshotPath);

            using var metaReader     = new StreamReader(metadataPath);
            using var sumReader      = new StreamReader(summaryPath);
            using var snapReader     = new StreamReader(snapshotPath);
            using var posReader      = new StreamReader(positionsPath);
            using var structReader   = new StreamReader(structurePath);

            var output = RunInMemory(family, isoWeek, runId,
                metaReader, sumReader, snapReader, posReader, structReader);

            WriteJson(_paths.DataLoaderOutput(isoWeek, runId), output);
            return output;
        }
        catch (DataLoaderHaltException halt)
        {
            WriteErrorFile(_paths.DataLoaderError(isoWeek, runId), halt, family, isoWeek, runId);
            throw;
        }
    }

    public DataLoaderOutput RunInMemory(
        string family, string isoWeek, string runId,
        TextReader metadataCsv, TextReader summaryCsv, TextReader snapshotCsv,
        TextReader positionsCsv, TextReader portfolioStructureMd)
    {
        var metadata  = _metadataParser.Parse(metadataCsv);
        var summary   = _summaryParser.Parse(summaryCsv);
        var snapshots = _snapshotParser.Parse(snapshotCsv);
        var positions = _positionsParser.Parse(positionsCsv);
        var structure = _portfolioParser.Parse(portfolioStructureMd);

        return Join(family, isoWeek, runId, metadata, summary, snapshots, positions, structure);
    }

    internal static void VerifyFilenameIsoWeekTags(string expected, params string[] paths)
    {
        foreach (var p in paths)
        {
            var fileName = Path.GetFileNameWithoutExtension(p);
            if (!fileName.Contains(expected, StringComparison.Ordinal))
            {
                throw new DataLoaderHaltException(
                    "filename_iso_week_mismatch",
                    $"File '{Path.GetFileName(p)}' does not contain expected iso_week '{expected}'.");
            }
        }
    }

    private static DataLoaderOutput Join(
        string family, string isoWeek, string runId,
        IReadOnlyList<FundMetadata> metadata,
        IReadOnlyDictionary<Isin, IReadOnlyList<NavBucket>> summary,
        IReadOnlyDictionary<Isin, FundSnapshot> snapshots,
        PositionsParseResult positions,
        PortfolioStructure structure)
    {
        var warnings = new List<string>(positions.Warnings);
        var metaByIsin = metadata.ToDictionary(m => m.Isin);
        var positionByIsin = positions.Holdings.ToDictionary(p => p.Isin);

        // Halt: any held ISIN must exist in metadata.
        foreach (var p in positions.Holdings)
        {
            if (!metaByIsin.ContainsKey(p.Isin))
            {
                throw new DataLoaderHaltException(
                    "held_isin_not_in_metadata",
                    $"positions.csv references ISIN '{p.Isin}' which is not present in metadata.");
            }
        }

        // Warn: any summary ISIN missing from metadata is dropped silently from funds[].
        foreach (var orphanIsin in summary.Keys.Where(k => !metaByIsin.ContainsKey(k)))
            warnings.Add($"summary.csv references ISIN '{orphanIsin}' not in metadata; orphan buckets dropped.");

        // Warn: any pinning that resolves to no metadata fund.
        var metaNames = new HashSet<string>(metadata.Select(m => m.Name), StringComparer.Ordinal);
        foreach (var p in structure.Pinnings)
        {
            var matchesByIsin = p.Isin.HasValue && metaByIsin.ContainsKey(p.Isin.Value);
            var matchesByName = !p.Isin.HasValue && p.Name != null && metaNames.Contains(p.Name);
            if (!matchesByIsin && !matchesByName)
            {
                var label = p.Isin?.Value ?? p.Name ?? "<empty>";
                warnings.Add($"portfolio_structure.md pins '{label}' but no metadata fund matches; pinning skipped.");
            }
        }

        var funds = new List<FundRecord>(metadata.Count);
        var frozen = new List<FrozenPosition>();

        foreach (var m in metadata)
        {
            var pinned = ResolvePinning(m, structure);

            if (pinned == PinnedLayer.Writeoff)
            {
                if (positionByIsin.TryGetValue(m.Isin, out var pos))
                {
                    frozen.Add(new FrozenPosition
                    {
                        Name = m.Name,
                        Isin = m.Isin,
                        CurrentValueKr = pos.CurrentValueKr,
                        CostBasisKr = pos.CostBasisKr,
                        Reason = "frozen — cannot trade",
                    });
                }
                continue;
            }

            var layer = pinned == PinnedLayer.Core ? FundLayer.Core : FundLayer.Active;

            var buckets = summary.TryGetValue(m.Isin, out var b) ? b : Array.Empty<NavBucket>();
            var snap = snapshots.TryGetValue(m.Isin, out var s) ? s : null;
            if (snap is null)
                warnings.Add($"snapshot.csv missing row for ISIN '{m.Isin}'; snapshot set to null.");

            positionByIsin.TryGetValue(m.Isin, out var heldPos);

            funds.Add(new FundRecord
            {
                Isin = m.Isin,
                Metadata = m,
                NavBuckets = buckets,
                Snapshot = snap,
                CurrentlyHeld = heldPos != null,
                CurrentValueKr = heldPos?.CurrentValueKr,
                CostBasisKr = heldPos?.CostBasisKr,
                Layer = layer,
            });
        }

        var coreCount = funds.Count(f => f.Layer == FundLayer.Core);
        var summaryRows = summary.Values.Sum(v => v.Count);
        var positionsRows = positions.TotalRowCount;

        return new DataLoaderOutput
        {
            GeneratedAt = DateTimeOffset.UtcNow.ToString("o"),
            IsoWeek = isoWeek,
            Family = family,
            RunId = runId,
            ConfigVersion = "1.0.0",
            Funds = funds,
            FrozenPositions = frozen,
            CashAvailableKr = positions.CashAvailableKr,
            DataQuality = new DataQuality
            {
                MetadataRows = metadata.Count,
                SummaryRows = summaryRows,
                SnapshotRows = snapshots.Count,
                PositionsRows = positionsRows,
                WriteoffCount = frozen.Count,
                CoreCount = coreCount,
                Warnings = warnings,
            },
        };
    }

    private static PinnedLayer? ResolvePinning(FundMetadata m, PortfolioStructure structure)
    {
        // ISIN takes precedence: scan ISIN-keyed pinnings first.
        foreach (var p in structure.Pinnings)
        {
            if (p.Isin != null && p.Isin == m.Isin)
                return p.Layer;
        }
        // Fall back to name-only pinnings.
        foreach (var p in structure.Pinnings)
        {
            if (p.Isin == null && p.Name != null && p.Name == m.Name)
                return p.Layer;
        }
        return null;
    }

    private static void WriteJson(string path, DataLoaderOutput output)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(output, JsonOptions.Default));
    }

    private static void WriteErrorFile(string path, DataLoaderHaltException halt, string family, string isoWeek, string runId)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var error = new
        {
            generated_at = DateTimeOffset.UtcNow.ToString("o"),
            iso_week = isoWeek,
            family,
            run_id = runId,
            error = new { trigger = halt.Trigger, message = halt.Message },
        };
        File.WriteAllText(path, JsonSerializer.Serialize(error, JsonOptions.Default));
    }
}
