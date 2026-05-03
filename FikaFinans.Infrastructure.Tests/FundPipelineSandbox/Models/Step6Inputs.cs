namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Models;

/// <summary>
/// Inputs to the Step 6 action consolidation agent. The signals JSON, positions
/// CSV, and portfolio structure markdown are all read from disk and inlined into
/// the prompt — Step 6 makes no Code Interpreter session and uploads no files.
/// </summary>
public sealed record Step6Inputs(
    string SignalsJsonPath,
    string PositionsCsvPath,
    string PortfolioStructurePath,
    decimal CashAvailableKr,
    string ModelDeploymentName);
