using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Application.Pipeline.Steps;

public interface IStep01DataLoader
{
    Task Step01LoadFundAsync(NavChangeSignal signal, CancellationToken ct = default);
}
