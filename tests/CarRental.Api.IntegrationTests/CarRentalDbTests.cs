using CarRental.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.IntegrationTests;

/// <summary>Schemat bazy na SQL Server: seed, indeksy, ograniczenia (unikalność, Restrict), konwersja statusu.</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Db")]
public sealed class CarRentalDbTests(SqlServerFixture sql)
{
    [Fact]
    public async Task InitializeDatabase_EmptyServer_CreatesSchemaWithTwelveSeededCars()
    {
        await using var db = await sql.CreateDatabaseAsync();

        var count = await db.Cars.CountAsync();

        Assert.Equal(12, count);
        Assert.Equal(SeedData.Cars.Length, count);
        Assert.True(await db.Cars.AllAsync(c => c.IsActive));
    }

    [Fact]
    public async Task InitializeDatabase_EmptyServer_CreatesQueryIndexes()
    {
        await using var db = await sql.CreateDatabaseAsync();

        var indexes = await db.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sys.indexes WHERE name LIKE 'IX[_]%'")
            .ToListAsync();

        Assert.Contains("IX_Rentals_Status_CreatedAtUtc", indexes);
        Assert.Contains("IX_Offers_ExpiresAtUtc", indexes);
        Assert.Contains("IX_Rentals_OfferId", indexes);
        Assert.Contains("IX_Cars_Make_Model", indexes);
    }

    [Fact]
    public async Task SaveChanges_SecondRentalForSameOffer_ThrowsDbUpdateException()
    {
        await using var db = await sql.CreateDatabaseAsync();
        var offer = TestData.Offer();
        db.Offers.Add(offer);
        db.Rentals.Add(TestData.RentalFor(offer));
        await db.SaveChangesAsync();

        // Drugi wniosek przychodzi w osobnym kontekście (jak drugie żądanie HTTP), więc sprawdza to baza, a nie EF
        await using var second = sql.CreateContext(SqlServerFixture.DatabaseName(db));
        second.Rentals.Add(TestData.RentalFor(offer));

        // Unikalny indeks na Rentals.OfferId: jedna oferta = najwyżej jeden wynajem
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_PendingRental_StoresStatusAsText()
    {
        await using var db = await sql.CreateDatabaseAsync();
        var offer = TestData.Offer();
        var rental = TestData.RentalFor(offer);
        db.Offers.Add(offer);
        db.Rentals.Add(rental);
        await db.SaveChangesAsync();

        var status = await db.Database
            .SqlQuery<string>($"SELECT Status AS Value FROM Rentals WHERE Id = {rental.Id}")
            .SingleAsync();

        Assert.Equal("Pending", status);
    }

    [Fact]
    public async Task Remove_CarWithOffers_ThrowsDbUpdateExceptionAndKeepsCar()
    {
        await using var db = await sql.CreateDatabaseAsync();
        db.Offers.Add(TestData.Offer(carId: 1));
        await db.SaveChangesAsync();
        var databaseName = SqlServerFixture.DatabaseName(db);

        // Osobny kontekst bez śledzonej oferty, więc DELETE trafia do bazy i blokuje go klucz obcy
        await using var delete = sql.CreateContext(databaseName);
        var car = await delete.Cars.SingleAsync(c => c.Id == 1);
        delete.Cars.Remove(car);

        // OnDelete(Restrict): auta z ofertami nie da się usunąć, zamiast tego wycofujemy je flagą IsActive
        await Assert.ThrowsAsync<DbUpdateException>(() => delete.SaveChangesAsync());

        await using var check = sql.CreateContext(databaseName);
        Assert.True(await check.Cars.AnyAsync(c => c.Id == 1));
    }
}
