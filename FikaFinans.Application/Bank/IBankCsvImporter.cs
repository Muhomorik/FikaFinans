namespace FikaFinans.Application.Bank;

public interface IBankCsvImporter
{
    /// <summary>
    /// Imports fund positions from a CSV file into the bank database.
    /// Skips import if holdings already exist (initial-load guard).
    /// </summary>
    Task ImportAsync(string csvPath, CancellationToken ct = default);

    /// <summary>
    /// Force-reimports all positions from CSV, replacing any existing holdings.
    /// </summary>
    Task ReimportAsync(string csvPath, CancellationToken ct = default);
}
