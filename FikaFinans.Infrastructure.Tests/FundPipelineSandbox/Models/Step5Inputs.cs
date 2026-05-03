namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Inputs to the Step 5 fund signal scoring agent.
/// <paramref name="FixtureFolder"/> must contain all seven canonical files
/// listed in <see cref="FikaFinans.Application.Agents.FundDataFiles"/>.
/// </summary>
public sealed record Step5Inputs(
    string FixtureFolder,
    string ModelDeploymentName);
