using FikaFinans.Domain.Funds;
using FikaFinans.Domain.Identifiers;

using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Application.Pipeline.Agents;

public interface IDataLoaderAgent
{
    DataLoaderOutput Run(string family, string isoWeek, PipelineRunId runId);

    /// <summary>
    /// Joins already-parsed inputs. Pure and storage-agnostic — reads nothing, writes nothing, so
    /// the caller owns both ends of the read-modify-write and catches
    /// <c>DataLoaderHaltException</c> itself. Every input is a domain model on purpose: the loader
    /// must not know whether buckets came from a CSV, the SQLite mirror or a REST call.
    /// </summary>
    DataLoaderOutput RunInMemory(
        Company family, IsoWeek isoWeek, PipelineRunId runId,
        IReadOnlyList<FundMetadata> metadata,
        IReadOnlyDictionary<Isin, IReadOnlyList<NavBucket>> summary,
        IReadOnlyDictionary<Isin, FundSnapshot> snapshots,
        PositionsParseResult positions,
        PortfolioStructure structure);
}
