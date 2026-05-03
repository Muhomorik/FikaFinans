using FikaFinans.Domain.Models;

namespace FikaFinans.Application.Agents;

/// <summary>
/// Port for one model's fund-analytics agent. One concrete instance per registered model
/// (gpt-5.4, Grok 4, DeepSeek-R1) — same adapter, different <c>ModelId</c>.
/// </summary>
public interface IFundAnalyticsAgent
{
    /// <summary>The Foundry model deployment id this instance is bound to (e.g. <c>"gpt-5.4"</c>).</summary>
    string ModelId { get; }

    /// <summary>
    /// Run the fund-analytics question through this model. Files are uploaded
    /// implicitly via <see cref="IFoundryFileStore.EnsureFilesUploadedAsync"/> — no per-call
    /// file arguments needed.
    /// </summary>
    Task<FundAnalyticsRun> RunAsync(string question, CancellationToken ct = default);
}
