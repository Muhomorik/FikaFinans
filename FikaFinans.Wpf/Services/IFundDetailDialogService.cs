namespace FikaFinans.Wpf.Services;

/// <summary>Opens the per-fund detail modal (all 10 pipeline steps aggregated for one ISIN).</summary>
public interface IFundDetailDialogService
{
    void Show(string fundIsin, string isoWeek, string runId, System.Windows.Window? owner = null);
}
