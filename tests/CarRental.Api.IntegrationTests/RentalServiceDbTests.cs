using CarRental.Api.Contracts;
using CarRental.Api.Domain;
using CarRental.Api.Messaging;
using CarRental.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarRental.Api.IntegrationTests;

/// <summary>
/// RentalService na prawdziwym SQL Server: transakcja Serializable w strategii z EnableRetryOnFailure.
/// Tego SQLite w testach endpointów nie sprawdzi.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Db")]
public sealed class RentalServiceDbTests(SqlServerFixture sql)
{
    private static CreateRentalRequest Request(Guid offerId) => new()
    {
        OfferId = offerId,
        FirstName = "Jan",
        LastName = "Kowalski",
        Email = "jan.kowalski@example.com"
    };

    [Fact]
    public async Task CreateAsync_ValidOffer_SavesPendingRentalInSerializableTransaction()
    {
        await using var db = await sql.CreateDatabaseAsync();
        var offer = TestData.Offer();
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var service = new RentalService(db, TimeProvider.System, NullLogger<RentalService>.Instance, new InProcessRentalRequestPublisher());

        var result = await service.CreateAsync(Request(offer.Id), "OurBroker", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RentalStatus.Pending, result.Value!.Status);
        var saved = await db.Rentals.AsNoTracking().SingleAsync(r => r.Id == result.Value.RentalId);
        Assert.Equal(offer.Id, saved.OfferId);
        Assert.Equal(offer.TotalPrice, saved.TotalPrice);
    }

    [Fact]
    public async Task CreateAsync_OverlappingOfferForSameCar_ReturnsConflict()
    {
        await using var db = await sql.CreateDatabaseAsync();
        var first = TestData.Offer(carId: 2);
        var second = TestData.Offer(carId: 2);
        db.Offers.AddRange(first, second);
        await db.SaveChangesAsync();
        var service = new RentalService(db, TimeProvider.System, NullLogger<RentalService>.Instance, new InProcessRentalRequestPublisher());

        var created = await service.CreateAsync(Request(first.Id), "OurBroker", CancellationToken.None);
        var conflict = await service.CreateAsync(Request(second.Id), "OurBroker", CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal(ServiceErrorType.Conflict, conflict.Error!.Type);
        Assert.Equal(1, await db.Rentals.CountAsync(r => r.CarId == 2));
    }
}
