using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarRental.Api.Data;

/// <summary>
/// Fabryka używana tylko przez narzędzia EF (dotnet ef migrations add, migrations bundle).
/// Dzięki niej narzędzia nie uruchamiają Program.cs (bez kluczy API, Entra i procesu w tle).
/// Connection string: zmienna CARRENTAL_DB_CONNECTION albo ConnectionStrings__CarRentalDb, inaczej LocalDB.
/// Bundle w pipeline i tak dostaje connection string parametrem --connection.
/// </summary>
public sealed class CarRentalDbContextFactory : IDesignTimeDbContextFactory<CarRentalDbContext>
{
    public const string ConnectionEnvVariable = "CARRENTAL_DB_CONNECTION";

    private const string DefaultConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=CarRentalDb;Trusted_Connection=True;TrustServerCertificate=True";

    public CarRentalDbContext CreateDbContext(string[] args)
    {
        var connectionString = FirstNotEmpty(
            Environment.GetEnvironmentVariable(ConnectionEnvVariable),
            Environment.GetEnvironmentVariable("ConnectionStrings__CarRentalDb"))
            ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<CarRentalDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new CarRentalDbContext(options);
    }

    private static string? FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
