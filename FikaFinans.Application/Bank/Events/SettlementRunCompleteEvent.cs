namespace FikaFinans.Application.Bank.Events;

public sealed record SettlementRunCompleteEvent(
    DateTimeOffset SettlementTime,
    int OrdersSettled);
