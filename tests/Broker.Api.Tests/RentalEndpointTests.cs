using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Api.Contracts;

namespace Broker.Api.Tests;

/// <summary>Wynajem przez Brokera (POST/GET /api/v1/rentals, zwrot) z fałszywym Car Rental API.</summary>
public sealed class RentalEndpointTests : IAsyncLifetime
{
    private readonly FakeCarRentalHandler _carRental = new();
    private readonly BrokerApiFactory _factory;

    public RentalEndpointTests()
    {
        _factory = new BrokerApiFactory(() => new ForwardingHandler(_carRental));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task PostRentals_Anonymous_Returns401()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRentals_WithoutCsrfHeader_Returns400AndDoesNotCallCarRentalApi()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var quoteId = await CreateQuoteAsync(client);
        using var noCsrf = _factory.CreateBrowserClient(withCsrfHeader: false);
        await BrokerApiFactory.LoginAsync(noCsrf, "jan");

        using var response = await noCsrf.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(_carRental.Requests, r => r.Path == "/api/v1/rentals");
    }

    [Fact]
    public async Task PostRentals_OwnQuote_Returns202PendingAndSavesRental()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var quoteId = await CreateQuoteAsync(client);

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rental = await response.Content.ReadFromJsonAsync<RentalResponse>(BrokerApiFactory.Json);
        Assert.NotNull(rental);
        Assert.Equal(RentalStatus.Pending, rental.Status);
        Assert.Equal("our", rental.Provider);
        Assert.Equal(_carRental.RentalId.ToString(), rental.ProviderRentalId);
        Assert.Equal(540m, rental.TotalPrice);
        Assert.False(rental.CanRequestReturn);
        Assert.EndsWith($"/api/v1/rentals/{rental.RentalId}", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);

        var sent = _carRental.Requests.Single(r => r.Method == HttpMethod.Post && r.Path == "/api/v1/rentals");
        Assert.Equal(BrokerApiFactory.OurBrokerKey, sent.ApiKey);
        var body = JsonDocument.Parse(sent.Body!).RootElement;
        var offerId = _factory.WithDb(db => db.Quotes.Single(q => q.Id == quoteId).ProviderOfferId);
        Assert.Equal(offerId, body.GetProperty("offerId").GetString());
        Assert.Equal("jan.kowalski@example.com", body.GetProperty("email").GetString());

        var saved = _factory.WithDb(db => db.Rentals.Single());
        Assert.Equal(quoteId, saved.QuoteId);
        Assert.Equal("Jan", saved.FirstName);
    }

    [Fact]
    public async Task PostRentals_QuoteOfAnotherUser_Returns404()
    {
        using var owner = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(owner, "jan");
        var quoteId = await CreateQuoteAsync(owner);
        using var other = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(other, "ola");

        using var response = await other.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(_carRental.Requests, r => r.Path == "/api/v1/rentals");
    }

    [Fact]
    public async Task PostRentals_AnonymousQuote_Returns404()
    {
        using var anonymous = _factory.CreateBrowserClient();
        var quoteId = await CreateQuoteAsync(anonymous);
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostRentals_ExpiredQuote_Returns410()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var quoteId = await CreateQuoteAsync(client);
        _factory.WithDb(db =>
        {
            db.Quotes.Single(q => q.Id == quoteId).ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            return db.SaveChanges();
        });

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(_carRental.Requests, r => r.Path == "/api/v1/rentals");
    }

    [Fact]
    public async Task PostRentals_SameQuoteTwice_Returns409()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var quoteId = await CreateQuoteAsync(client);
        using var first = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        using var second = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task PostRentals_CarRentalApiRejectsWith409_Returns409WithItsMessage()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var quoteId = await CreateQuoteAsync(client);
        _carRental.Responder = _ => FakeCarRentalHandler.Json(HttpStatusCode.Conflict,
            new { title = "Samochód został już wynajęty w tym terminie.", status = 409 });

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Samochód został już wynajęty w tym terminie.", problem.GetProperty("title").GetString());
        Assert.Equal(0, _factory.WithDb(db => db.Rentals.Count()));
    }

    [Fact]
    public async Task PostRentals_InvalidEmail_Returns400ForEmail()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");

        using var response = await client.PostAsJsonAsync("/api/v1/rentals", new
        {
            quoteId = Guid.NewGuid(),
            firstName = "Jan",
            lastName = "Kowalski",
            email = "to-nie-email"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task GetRentals_PendingRental_RefreshesStatusFromCarRentalApi()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        await RentAsync(client);

        var list = await client.GetFromJsonAsync<List<RentalResponse>>("/api/v1/rentals", BrokerApiFactory.Json);

        var rental = Assert.Single(list!);
        Assert.Equal(RentalStatus.Confirmed, rental.Status);
        Assert.True(rental.CanRequestReturn);
        Assert.Contains(_carRental.Requests, r => r.Method == HttpMethod.Get && r.Path == $"/api/v1/rentals/{_carRental.RentalId}");
        Assert.Equal(RentalStatus.Confirmed, _factory.WithDb(db => db.Rentals.Single().Status));
    }

    [Fact]
    public async Task GetRentals_CarRentalApiDown_KeepsLastKnownStatus()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        await RentAsync(client);
        _carRental.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var list = await client.GetFromJsonAsync<List<RentalResponse>>("/api/v1/rentals", BrokerApiFactory.Json);

        Assert.Equal(RentalStatus.Pending, Assert.Single(list!).Status);
    }

    [Fact]
    public async Task GetRentals_OtherUser_SeesOnlyOwnRentals()
    {
        using var owner = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(owner, "jan");
        var rental = await RentAsync(owner);
        using var other = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(other, "ola");

        var list = await other.GetFromJsonAsync<List<RentalResponse>>("/api/v1/rentals", BrokerApiFactory.Json);
        using var details = await other.GetAsync($"/api/v1/rentals/{rental.RentalId}");

        Assert.Empty(list!);
        Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
    }

    [Fact]
    public async Task GetRental_Own_ReturnsFreshStatus()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var created = await RentAsync(client);

        var rental = await client.GetFromJsonAsync<RentalResponse>($"/api/v1/rentals/{created.RentalId}", BrokerApiFactory.Json);

        Assert.NotNull(rental);
        Assert.Equal(RentalStatus.Confirmed, rental.Status);
        Assert.Equal(created.CarLabel, rental.CarLabel);
    }

    [Fact]
    public async Task PostReturn_ConfirmedRental_SendsCustomerEmailAndReturnsReturnRequested()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var created = await RentAsync(client);

        using var response = await client.PostAsync($"/api/v1/rentals/{created.RentalId}/return", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rental = await response.Content.ReadFromJsonAsync<RentalResponse>(BrokerApiFactory.Json);
        Assert.Equal(RentalStatus.ReturnRequested, rental!.Status);
        var sent = _carRental.Requests.Single(r => r.Path == $"/api/v1/rentals/{_carRental.RentalId}/return");
        Assert.Equal("jan.kowalski@example.com", JsonDocument.Parse(sent.Body!).RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task PostReturn_RentalStillPending_Returns409()
    {
        _carRental.RentalStatusOnGet = "Pending";
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var created = await RentAsync(client);

        using var response = await client.PostAsync($"/api/v1/rentals/{created.RentalId}/return", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain(_carRental.Requests, r => r.Path.EndsWith("/return", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostReturn_RentalOfAnotherUser_Returns404()
    {
        using var owner = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(owner, "jan");
        var created = await RentAsync(owner);
        using var other = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(other, "ola");

        using var response = await other.PostAsync($"/api/v1/rentals/{created.RentalId}/return", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<RentalResponse> RentAsync(HttpClient client)
    {
        var quoteId = await CreateQuoteAsync(client);
        using var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(quoteId));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RentalResponse>(BrokerApiFactory.Json))!;
    }

    private static async Task<Guid> CreateQuoteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/offers", new
        {
            provider = "our",
            carId = "1",
            startDate = Today.AddDays(7),
            endDate = Today.AddDays(10),
            age = 30,
            licenseYears = 8,
            location = "Warszawa"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<QuoteResult>(BrokerApiFactory.Json);
        return Assert.Single(result!.Offers).QuoteId;
    }

    private static object RentalBody(Guid quoteId) => new
    {
        quoteId,
        firstName = "Jan",
        lastName = "Kowalski",
        email = "jan.kowalski@example.com"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();
}
