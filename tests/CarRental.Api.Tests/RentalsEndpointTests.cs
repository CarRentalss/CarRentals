using System.Net;
using System.Net.Http.Json;
using CarRental.Api.Contracts;
using CarRental.Api.Domain;

namespace CarRental.Api.Tests;

public class RentalsEndpointTests
{
    [Fact]
    public async Task CreateRental_ValidOffer_Returns202Pending()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rental = await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(CarRentalApiFactory.Json);
        Assert.NotNull(rental);
        Assert.Equal(RentalStatus.Pending, rental.Status);
        Assert.NotNull(response.Headers.Location);
        Assert.EndsWith($"/api/v1/rentals/{rental.RentalId}", response.Headers.Location.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateRental_InvalidEmail_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId, email: "to-nie-jest-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_UnknownOffer_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_ExpiredOffer_Returns410()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_OfferStillValidAfter9Minutes()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);

        factory.Clock.Advance(TimeSpan.FromMinutes(9));
        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_SameOfferTwice_Returns409()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);
        await TestApi.CreateRentalAsync(client, offer.OfferId);

        var response = await client.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_CarTakenInTheMeantime_Returns409()
    {
        // Dwóch klientów dostało ofertę na to samo auto i termin, wynajmuje ten, kto pierwszy złoży wniosek
        await using var factory = new CarRentalApiFactory();
        var ourBroker = factory.CreateBrokerClient();
        var otherBroker = factory.CreateBrokerClient(CarRentalApiFactory.OtherBrokerKey);
        var firstOffer = await TestApi.CreateOfferAsync(ourBroker);
        var secondOffer = await TestApi.CreateOfferAsync(otherBroker);

        await TestApi.CreateRentalAsync(ourBroker, firstOffer.OfferId);
        var response = await otherBroker.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(secondOffer.OfferId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateRental_OfferOfAnotherBroker_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var ourBroker = factory.CreateBrokerClient();
        var otherBroker = factory.CreateBrokerClient(CarRentalApiFactory.OtherBrokerKey);
        var offer = await TestApi.CreateOfferAsync(ourBroker);

        var response = await otherBroker.PostAsJsonAsync("/api/v1/rentals", TestApi.RentalBody(offer.OfferId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRental_BeforeAndAfterBackgroundConfirmation()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);
        var created = await TestApi.CreateRentalAsync(client, offer.OfferId);

        var before = await client.GetFromJsonAsync<RentalStatusResponse>($"/api/v1/rentals/{created.RentalId}", CarRentalApiFactory.Json);
        await factory.ConfirmPendingRentalsAsync();
        var after = await client.GetFromJsonAsync<RentalStatusResponse>($"/api/v1/rentals/{created.RentalId}", CarRentalApiFactory.Json);

        Assert.Equal(RentalStatus.Pending, before!.Status);
        Assert.Equal(RentalStatus.Confirmed, after!.Status);
        Assert.Null(after.Reason);
        Assert.Equal(offer.TotalPrice, after.TotalPrice);
        Assert.Equal(TestApi.Start, after.StartDate);
    }

    [Fact]
    public async Task GetRental_OfAnotherBroker_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var ourBroker = factory.CreateBrokerClient();
        var otherBroker = factory.CreateBrokerClient(CarRentalApiFactory.OtherBrokerKey);
        var offer = await TestApi.CreateOfferAsync(ourBroker);
        var created = await TestApi.CreateRentalAsync(ourBroker, offer.OfferId);

        var response = await otherBroker.GetAsync($"/api/v1/rentals/{created.RentalId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetRental_Unknown_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.GetAsync($"/api/v1/rentals/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestReturn_ConfirmedRental_ReturnsReturnRequested()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var rentalId = await TestApi.CreateConfirmedRentalAsync(factory, client);

        var response = await client.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return", new { email = "JAN.KOWALSKI@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(CarRentalApiFactory.Json);
        Assert.Equal(RentalStatus.ReturnRequested, body!.Status);
    }

    [Fact]
    public async Task RequestReturn_WrongEmail_Returns403()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var rentalId = await TestApi.CreateConfirmedRentalAsync(factory, client);

        var response = await client.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return", new { email = "ktos.inny@example.com" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequestReturn_StillPending_Returns409()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var offer = await TestApi.CreateOfferAsync(client);
        var created = await TestApi.CreateRentalAsync(client, offer.OfferId);

        var response = await client.PostAsJsonAsync($"/api/v1/rentals/{created.RentalId}/return", new { email = "jan.kowalski@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RequestReturn_Twice_Returns409()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var rentalId = await TestApi.CreateConfirmedRentalAsync(factory, client);
        await client.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return", new { email = "jan.kowalski@example.com" });

        var response = await client.PostAsJsonAsync($"/api/v1/rentals/{rentalId}/return", new { email = "jan.kowalski@example.com" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
