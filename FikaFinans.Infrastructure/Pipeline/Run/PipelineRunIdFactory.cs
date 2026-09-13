using System.Globalization;

using FikaFinans.Application.Pipeline.Run;
using FikaFinans.Domain.Identifiers;
using FikaFinans.Domain.Pipeline;

namespace FikaFinans.Infrastructure.Pipeline.Run;

/// <summary>
/// Default <see cref="IPipelineRunIdFactory"/>, producing
/// <c>{navDate:yyyyMMdd}-{isin}-{8 hex}</c> — see <see cref="PipelineRunId"/> for what
/// each segment buys.
/// </summary>
/// <remarks>
/// No clock of its own: the date comes from the signal that triggered the run, so the id
/// names the trading date it processed rather than the wall-clock moment it happened to
/// start. Only the suffix is non-deterministic.
/// </remarks>
public sealed class PipelineRunIdFactory : IPipelineRunIdFactory
{
    /// <summary>
    /// Half a GUID. Enough to separate retries of one fund on one trading date, which is
    /// the only collision this has to survive — the date and ISIN segments already
    /// partition everything else.
    /// </summary>
    private const int SuffixLength = 8;

    /// <inheritdoc />
    public PipelineRunId NewRunId(Isin isin, DateTimeOffset navDate)
    {
        var suffix = Guid.NewGuid().ToString("N")[..SuffixLength];

        return new PipelineRunId(string.Create(
            CultureInfo.InvariantCulture,
            $"{navDate:yyyyMMdd}-{isin.Value}-{suffix}"));
    }
}
