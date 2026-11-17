using CarRental.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace CarRental.Api.IntegrationTests;

/// <summary>
/// Jeden kontener SQL Server 2022 na wszystkie testy z kolekcji "SqlServer".
/// Każdy test dostaje własną, pustą bazę (inna nazwa), więc testy nie widzą swoich danych.
/// L5: testy integracyjne na izolowanej instancji tego samego silnika co produkcja.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    // Konstruktor z obrazem (Testcontainers 4.10+), bezparametrowy jest oznaczony jako przestarzały
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Kontekst na nowej bazie, utworzonej tak jak przy starcie API (migracje albo EnsureCreated).</summary>
    public async Task<CarRentalDbContext> CreateDatabaseAsync()
    {
        var db = CreateContext($"CarRental_{Guid.NewGuid():N}");
        await CreateSchemaAsync(db);
        return db;
    }

    /// <summary>Drugi kontekst na tej samej bazie (np. żeby sprawdzić zapis bez cache'u EF).</summary>
    public CarRentalDbContext CreateContext(string databaseName)
    {
        var connection = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName
        };

        var options = new DbContextOptionsBuilder<CarRentalDbContext>()
            .UseSqlServer(connection.ConnectionString, sql => sql.EnableRetryOnFailure())
            .Options;

        return new CarRentalDbContext(options);
    }

    /// <summary>Nazwa bazy, na której pracuje kontekst.</summary>
    public static string DatabaseName(CarRentalDbContext db) =>
        new SqlConnectionStringBuilder(db.Database.GetConnectionString()!).InitialCatalog;

    // To samo co InitializeDatabaseAsync w Program.cs: jak są migracje, to Migrate, a jak nie ma, to EnsureCreated
    private static async Task CreateSchemaAsync(CarRentalDbContext db)
    {
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
