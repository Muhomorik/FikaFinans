using FikaFinans.Application.Pipeline.Configs;
using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Portfolio;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IPortfolioConstructorAgent
{
    TradesOutput Run(string isoWeek, PipelineRunId runId, string? macroRegime = null);

    /// <summary>
    /// Same processing as <see cref="Run"/> but the Step 9 input is supplied
    /// by the caller instead of read from disk. The disk write of the Step 10
    /// output stays for now — WPF still reads it (until Phase 8 sub-step 8c).
    /// Used by the streaming runner so the input path goes through the SQLite
    /// IsinProgress columns instead of the Step 9 disk file.
    /// </summary>
    TradesOutput RunFromInput(
        DataLoaderOutput step9Input,
        string isoWeek,
        PipelineRunId runId,
        string? macroRegime = null);

    /// <summary>
    /// Constructs the trades for records and a portfolio already read. Pure and
    /// storage-agnostic — reads nothing, writes nothing, so the caller owns both ends. Every
    /// input is a domain model on purpose: the constructor must not know whether the records
    /// came from a JSON file, progress rows or a queue message, nor where the holdings did.
    /// </summary>
    /// <remarks>
    /// The passes as in <see cref="Run"/>, over <paramref name="funds"/> as the universe. The
    /// portfolio-wide fields no per-fund record carries — <paramref name="frozenPositions"/>,
    /// <paramref name="cashAvailableKr"/> — come in beside them, and each record's held and
    /// value fields are taken as given, so the caller decides how fresh they are. The
    /// <c>config_version</c> is checked here, so a bad config throws.
    /// <paramref name="macroRegime"/> is the cash-floor override table's key, unmapped.
    /// Synchronous, as step 8's: the passes await nothing.
    /// </remarks>
    TradesOutput RunInMemory(
        IsoWeek isoWeek,
        IReadOnlyList<FundRecord> funds,
        IReadOnlyList<FrozenPosition> frozenPositions,
        decimal cashAvailableKr,
        PortfolioConstructorConfig config,
        string? macroRegime = null);
}
