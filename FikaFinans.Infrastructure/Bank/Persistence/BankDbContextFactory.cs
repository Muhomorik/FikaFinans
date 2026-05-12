using Microsoft.EntityFrameworkCore;

namespace FikaFinans.Infrastructure.Bank.Persistence;

/// <summary>
/// Plain <see cref="IDbContextFactory{TContext}"/> over a captured
/// <see cref="DbContextOptions{TContext}"/>. Each call to
/// <see cref="CreateDbContext"/> returns a fresh, independent context.
/// </summary>
/// <remarks>
/// We use this instead of EF Core's <c>AddPooledDbContextFactory</c> because
/// the WPF app wires DI through Autofac, not <c>IServiceCollection</c>, and
/// the bank simulator's load profile doesn't justify pooling.
/// </remarks>
public sealed class BankDbContextFactory : IDbContextFactory<BankDbContext>
{
    private readonly DbContextOptions<BankDbContext> _options;

    public BankDbContextFactory(DbContextOptions<BankDbContext> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public BankDbContext CreateDbContext() => new(_options);
}
