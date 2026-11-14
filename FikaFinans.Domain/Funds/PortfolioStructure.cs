using FikaFinans.Domain.Identifiers;

namespace FikaFinans.Domain.Funds;

public sealed class PortfolioStructure
{
    public required IReadOnlyList<PinnedFund> PinnedFunds { get; init; }

    /// <summary>
    /// The layer a fund is pinned to, or null when it is not pinned — an active position.
    /// One rule for every step that classifies funds: step 1 when it builds records, step 10
    /// when it reads the holdings.
    /// </summary>
    /// <remarks>
    /// A pin naming the ISIN wins over one naming the fund by name only; a name-only pin
    /// matches on the exact, case-sensitive name.
    /// </remarks>
    public PinnedLayer? LayerOf(Isin isin, string? name)
    {
        foreach (var p in PinnedFunds)
        {
            if (p.Isin != null && p.Isin == isin)
                return p.Layer;
        }

        if (name is null)
            return null;

        foreach (var p in PinnedFunds)
        {
            if (p.Isin == null && p.Name != null && p.Name == name)
                return p.Layer;
        }

        return null;
    }
}
