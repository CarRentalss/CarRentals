using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Broker.Api.Data;

/// <summary>
/// Używane tylko przez narzędzia EF (dotnet ef migrations add / migrations bundle),
/// żeby nie odpalać Program.cs (logowanie, dostawcy, konfiguracja z chmury).
/// Connection string: zmienna BROKER_DB_CONNECTION albo ConnectionStrings__BrokerDb, inaczej LocalDB.
/// </summary>
public sealed class BrokerDbContextFactory : IDesignTimeDbContextFactory<BrokerDbContext>
{
    private const string LocalDb =
        "Server=(localdb)\\MSSQLLocalDB;Database=BrokerDb;Trusted_Connection=True;TrustServerCertificate=True";

    public BrokerDbContext CreateDbContext(string[] args)
    {
        var connectionString = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BROKER_DB_CONNECTION"),
            Environment.GetEnvironmentVariable("ConnectionStrings__BrokerDb"))
            ?? LocalDb;

        var options = new DbContextOptionsBuilder<BrokerDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new BrokerDbContext(options);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
