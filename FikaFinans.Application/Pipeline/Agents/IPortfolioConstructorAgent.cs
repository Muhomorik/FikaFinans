using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Portfolio;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IPortfolioConstructorAgent
{
    TradesOutput Run(string isoWeek, string runId, string? macroRegime = null);

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
        string runId,
        string? macroRegime = null);
}
