namespace FikaFinans.Application.Bank;

/// <summary>
/// Tally returned by <see cref="ISendToBankService.SubmitAsync"/>. <c>Sent</c>
/// counts orders that <see cref="ITradingService"/> accepted; <c>Skipped</c>
/// counts trades that were dropped (Hold/NoOp, missing position, zero-unit
/// rounding, or rejected by the trading service). <c>Warnings</c> carries
/// per-skip diagnostic strings so callers (WPF status text, Function logs,
/// the eventual reconciliation step) can surface them uniformly.
/// </summary>
public sealed record SendToBankResult(
    int Sent,
    int Skipped,
    IReadOnlyList<string> Warnings);
