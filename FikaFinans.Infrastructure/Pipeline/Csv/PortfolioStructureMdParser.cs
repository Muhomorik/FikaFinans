using FikaFinans.Domain.Funds;

namespace FikaFinans.Infrastructure.Pipeline.Csv;

public sealed class PortfolioStructureMdParser
{
    public PortfolioStructure Parse(TextReader reader)
    {
        var pinnings = new List<PinnedFund>();

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!IsPipeRow(line)) continue;

            var cells = SplitRow(line);
            if (cells.Length < 4) continue;

            var isin  = string.IsNullOrWhiteSpace(cells[0]) ? null : cells[0];
            var name  = string.IsNullOrWhiteSpace(cells[1]) ? null : cells[1];
            var layer = cells[2].ToLowerInvariant();

            if (!TryParseLayer(layer, out var pinned)) continue;
            if (isin == null && name == null) continue;

            pinnings.Add(new PinnedFund { Isin = isin is null ? null : new(isin), Name = name, Layer = pinned });
        }

        return new PortfolioStructure { Pinnings = pinnings };
    }

    private static bool IsPipeRow(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith('|');
    }

    private static string[] SplitRow(string line)
    {
        var raw = line.Trim().Trim('|').Split('|');
        return raw.Select(c => c.Trim()).ToArray();
    }

    private static bool TryParseLayer(string raw, out PinnedLayer layer)
    {
        switch (raw)
        {
            case "core":
                layer = PinnedLayer.Core;
                return true;
            case "writeoff":
                layer = PinnedLayer.Writeoff;
                return true;
            default:
                layer = default;
                return false;
        }
    }
}
