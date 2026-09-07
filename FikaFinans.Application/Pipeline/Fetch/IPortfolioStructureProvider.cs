using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Fetch;

/// <summary>
/// Source seam for the hand-written layer overrides — which funds are pinned <c>core</c> or
/// <c>writeoff</c>, everything else being an active position. Configuration rather than
/// producer data, so unlike <see cref="IFundMetadataProvider"/> it has no company or week
/// to scope by: one portfolio, one set of pinnings.
/// </summary>
/// <remarks>
/// A markdown file today. The seam exists because that is the one input with no home
/// outside a filesystem — an environment variable, a settings store or a config service can
/// each back it later without the caller noticing.
/// </remarks>
public interface IPortfolioStructureProvider
{
    /// <summary>
    /// Reads the current pinnings. Never null — a source with nothing to say returns an
    /// empty set, which simply means every fund is left as an active position.
    /// </summary>
    Task<PortfolioStructure> GetStructureAsync(CancellationToken ct = default);
}
