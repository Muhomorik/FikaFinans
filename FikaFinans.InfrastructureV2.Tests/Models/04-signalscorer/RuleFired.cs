namespace FikaFinans.InfrastructureV2.Tests.Models.SignalScorer;

public static class RuleFired
{
    public const string Buy3of3ZeroDd          = "buy_3of3_zero_dd";
    public const string SellSharpeNegative     = "sell_sharpe_negative";
    public const string SellDrawdownBreach     = "sell_drawdown_breach";
    public const string SellPosLe1             = "sell_pos_le_1";
    public const string SellCombined           = "sell_combined";
    public const string WatchPartialWithMacro  = "watch_partial_with_macro";
    public const string NeutralDefault         = "neutral_default";
    public const string NeutralInsufficient    = "neutral_insufficient_history";
    public const string NeutralConflicting     = "neutral_conflicting";
    public const string NeutralNoData          = "neutral_no_data";
}
