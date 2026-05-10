using FikaFinans.Application.Bank.Events;

namespace FikaFinans.Application.Bank;

public interface ISettlementEngine : IDisposable
{
    void Start();
    IObservable<OrderSettledEvent> OrderSettled { get; }
    IObservable<SettlementRunCompleteEvent> SettlementRunComplete { get; }
}
