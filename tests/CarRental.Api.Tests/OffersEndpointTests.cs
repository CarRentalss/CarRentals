using System.Net;
using System.Net.Http.Json;
using CarRental.Api.Contracts;

namespace CarRental.Api.Tests;

public class OffersEndpointTests
{
    [Fact]
    public async Task CreateOffer_Valid_Returns201WithPriceAndExpiry()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var offer = await response.Content.ReadFromJsonAsync<OfferResponse>(CarRentalApiFactory.Json);
        Assert.NotNull(offer);
        Assert.NotEqual(Guid.Empty, offer.OfferId);
        Assert.Equal(1, offer.CarId);
        Assert.Equal(150m, offer.DailyPrice);          // Toyota Corolla, doświadczony kierowca
        Assert.Equal(30m, offer.DailyInsurancePrice);
        Assert.Equal(3, offer.Days);
        Assert.Equal(540m, offer.TotalPrice);          // (150 + 30) * 3
        Assert.Equal("PLN", offer.Currency);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddMinutes(10), offer.ExpiresAt.ToUniversalTime());
    }

    [Fact]
    public async Task CreateOffer_YoungNewDriver_PaysMore()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var offer = await TestApi.CreateOfferAsync(client, TestApi.OfferBody(age: 20, licenseYears: 1));

        Assert.Equal(225m, offer.DailyPrice);          // 150 * 1.25 * 1.2
        Assert.Equal(45m, offer.DailyInsurancePrice);  // 30 * 1.25 * 1.2
    }

    [Fact]
    public async Task CreateOffer_WithoutApiKey_Returns401()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(16, 0)]   // za młody
    [InlineData(20, 5)]   // staż dłuższy niż wiek - 18
    public async Task CreateOffer_InvalidDriver_Returns400(int age, int licenseYears)
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody(age: age, licenseYears: licenseYears));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_MissingFields_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", new { carId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_StartInPast_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers",
            TestApi.OfferBody(start: new DateOnly(2026, 10, 1), end: new DateOnly(2026, 10, 3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_EndBeforeStart_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers",
            TestApi.OfferBody(start: TestApi.End, end: TestApi.Start));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_TooLong_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers",
            TestApi.OfferBody(start: TestApi.Start, end: TestApi.Start.AddDays(31)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_WrongLocation_Returns400()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody(location: "Kraków"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_LocationIsCaseInsensitive()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody(location: "warszawa"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_UnknownCar_Returns404()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();

        var response = await client.PostAsJsonAsync("/api/v1/offers", TestApi.OfferBody(carId: 999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_CarAlreadyRentedInThatTerm_Returns409()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var first = await TestApi.CreateOfferAsync(client);
        await TestApi.CreateRentalAsync(client, first.OfferId);

        var response = await client.PostAsJsonAsync("/api/v1/offers",
            TestApi.OfferBody(start: TestApi.Start.AddDays(1), end: TestApi.End.AddDays(2)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_TermStartingOnReturnDay_IsAllowed()
    {
        await using var factory = new CarRentalApiFactory();
        var client = factory.CreateBrokerClient();
        var first = await TestApi.CreateOfferAsync(client);
        await TestApi.CreateRentalAsync(client, first.OfferId);

        var response = await client.PostAsJsonAsync("/api/v1/offers",
            TestApi.OfferBody(start: TestApi.End, end: TestApi.End.AddDays(2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
