using System.Diagnostics;
using FikaFinans.Domain.Bank.Identifiers;

namespace FikaFinans.Domain.Bank.Accounts;

[DebuggerDisplay("{Name} ({Code}) - {Type}")]
public class Account
{
    public AccountId Id { get; private init; }
    public string Name { get; private init; } = string.Empty;
    public string Code { get; private init; } = string.Empty;
    public AccountType Type { get; private init; }
    public string Currency { get; private init; } = "SEK";

    private Account() { }

    public static Account Create(string name, string code, AccountType type, string currency = "SEK")
    {
        return new Account
        {
            Id = AccountId.NewId(),
            Name = name,
            Code = code,
            Type = type,
            Currency = currency
        };
    }

    public static Account CreateWithId(AccountId id, string name, string code, AccountType type, string currency = "SEK")
    {
        return new Account
        {
            Id = id,
            Name = name,
            Code = code,
            Type = type,
            Currency = currency
        };
    }
}
