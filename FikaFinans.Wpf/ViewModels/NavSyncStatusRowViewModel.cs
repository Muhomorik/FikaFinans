using System.Diagnostics;
using FikaFinans.Application.Pipeline.Signals;

namespace FikaFinans.Wpf.ViewModels;

/// <summary>
/// Display projection of a <see cref="NavSyncStatusRow"/> for the NAV Sync grid:
/// pre-formatted dates + status text, plus the raw <see cref="Kind"/> the
/// <c>NavSyncStatusToBrushConverter</c> colours the row by.
/// </summary>
[DebuggerDisplay("{Isin} {StatusText} (YR {YrNavDateText})")]
public sealed record NavSyncStatusRowViewModel(
    string Isin,
    string Name,
    string CompanyName,
    string YrNavDateText,
    string LastProcessedText,
    string StatusText,
    NavSyncStatusKind Kind,
    DateTimeOffset NavDate)
{
    /// <summary>Projects a domain status row into its display form ("—" for a missing anchor).</summary>
    public static NavSyncStatusRowViewModel From(NavSyncStatusRow row) => new(
        row.Isin.Value,
        row.Name,
        row.CompanyName,
        row.LatestNavDate.ToString("yyyy-MM-dd"),
        row.LastProcessedNavDate?.ToString("yyyy-MM-dd") ?? "—",
        StatusTextFor(row.Kind),
        row.Kind,
        row.LatestNavDate);

    private static string StatusTextFor(NavSyncStatusKind kind) => kind switch
    {
        NavSyncStatusKind.New => "New",
        NavSyncStatusKind.Changed => "Changed",
        NavSyncStatusKind.UpToDate => "Up to date",
        NavSyncStatusKind.Processing => "Processing",
        _ => kind.ToString(),
    };
}
