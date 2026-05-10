using FikaFinans.Domain.Bank.Common;
using FikaFinans.Domain.Bank.Identifiers;
using FluentResults;
using FikaFinans.Domain.Bank.Ledger;

namespace FikaFinans.Application.Bank;

public interface ILedgerService
{
    Task<Result<TransactionId>> PostTransactionAsync(
        string description,
        IReadOnlyList<(AccountId AccountId, decimal Debit, decimal Credit, string Currency)> entries,
        TradingOrderId? relatedOrderId = null,
        CancellationToken ct = default);

    Task<Money> GetAccountBalanceAsync(AccountId accountId, CancellationToken ct = default);

    Task<IReadOnlyList<Transaction>> GetAllTransactionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Transaction>> GetTransactionsByOrderAsync(
        TradingOrderId orderId, CancellationToken ct = default);
}
