using FikaFinans.Application.Pipeline.Signals;
using FikaFinans.Domain.Funds;

namespace FikaFinans.Application.Pipeline.Steps;

public interface IStep01DataLoader
{
    /// <summary>Marks the fund's progress row in-flight, before anything is read.</summary>
    Task BeginProcessingAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Reads the identity slice and the NAV history delta through the fetch seam.</summary>
    Task LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Computes the bucketed and rolling-window metrics from the mirrored series.</summary>
    Task AssembleAgentInputAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Joins the assembled inputs via <c>IDataLoaderAgent.RunInMemory</c>.</summary>
    Task<DataLoaderOutput> RunAgentAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Writes <c>Step01Json</c> on the progress row and the new raw NAV rows.</summary>
    Task PersistAsync(NavChangeSignal signal, CancellationToken ct = default);

    /// <summary>Emits the step-2 trigger — after the write, never before.</summary>
    Task EmitDoneAsync(NavChangeSignal signal, CancellationToken ct = default);
}
