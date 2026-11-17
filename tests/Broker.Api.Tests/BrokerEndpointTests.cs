using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Api.Contracts;

namespace Broker.Api.Tests;

/// <summary>Endpointy Brokera z podstawionym (fałszywym) Car Rental API.</summary>
public sealed class BrokerEndpointTests : IAsyncLifetime
{
    private readonly FakeCarRentalHandler _carRental = new();
    private readonly BrokerApiFactory _factory;

    public BrokerEndpointTests()
    {
        _factory = new BrokerApiFactory(() => new ForwardingHandler(_carRental));
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task GetHealth_Anonymous_Returns200()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetHealth_Anonymous_HasSecurityHeaders()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCars_DefaultPaging_ReturnsFiveOfTwelveWithApiKeySent()
    {
        using var client = _factory.CreateBrowserClient();

        var page = await client.GetFromJsonAsync<PagedResult<CarListItem>>("/api/v1/cars", BrokerApiFactory.Json);

        Assert.NotNull(page);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        var request = Assert.Single(_carRental.Requests);
        Assert.Equal("/api/v1/cars", request.Path);
        Assert.Equal(BrokerApiFactory.OurBrokerKey, request.ApiKey);
    }

    [Fact]
    public async Task GetCars_PassesHttpsImageUrlAndCreditFromCarRentalApi()
    {
        _carRental.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new object[]
            {
                new { id = 1, make = "Toyota", model = "Corolla", year = 2022, location = "Warszawa", available = true,
                      imageUrl = "https://st.blob.core.windows.net/cars/toyota-corolla-2022.jpg", imageCredit = "Autor, CC BY-SA 4.0, Wikimedia Commons" },
                new { id = 2, make = "Toyota", model = "Yaris", year = 2023, location = "Warszawa", available = true,
                      imageUrl = "javascript:alert(1)", imageCredit = (string?)null },
                new { id = 3, make = "Toyota", model = "RAV4", year = 2024, location = "Warszawa", available = true }
            })
        };
        using var client = _factory.CreateBrowserClient();

        var page = await client.GetFromJsonAsync<PagedResult<CarListItem>>("/api/v1/cars?make=Toyota", BrokerApiFactory.Json);

        var corolla = page!.Items.Single(c => c.CarId == "1");
        Assert.Equal("https://st.blob.core.windows.net/cars/toyota-corolla-2022.jpg", corolla.ImageUrl);
        Assert.Equal("Autor, CC BY-SA 4.0, Wikimedia Commons", corolla.ImageCredit);
        // Adres inny niż https nie trafia do img src na froncie
        Assert.Null(page.Items.Single(c => c.CarId == "2").ImageUrl);
        Assert.Null(page.Items.Single(c => c.CarId == "3").ImageUrl);
    }

    [Fact]
    public async Task GetCars_PageSizeTooLarge_Returns400WithCamelCaseField()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/api/v1/cars?pageSize=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("pageSize", out _));
    }

    [Fact]
    public async Task GetCars_CarRentalApiDown_ReturnsEmptyPageWithSkippedProvider()
    {
        _carRental.Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        using var client = _factory.CreateBrowserClient();

        var page = await client.GetFromJsonAsync<PagedResult<CarListItem>>("/api/v1/cars", BrokerApiFactory.Json);

        Assert.NotNull(page);
        Assert.Empty(page.Items);
        var skipped = Assert.Single(page.SkippedProviders);
        Assert.Equal("our", skipped.Provider);
        Assert.Equal(SkipReason.Error, skipped.Reason);
    }

    [Fact]
    public async Task GetMakes_Anonymous_ReturnsMakesFromCatalog()
    {
        using var client = _factory.CreateBrowserClient();

        var makes = await client.GetFromJsonAsync<List<MakeModels>>("/api/v1/cars/makes", BrokerApiFactory.Json);

        Assert.NotNull(makes);
        Assert.Equal(10, makes.Count);
        Assert.Equal(new[] { "Model1", "Model2", "Model3" }, makes.Single(m => m.Make == "Toyota").Models);
    }

    [Fact]
    public async Task PostOffers_WithoutCsrfHeader_Returns400()
    {
        using var client = _factory.CreateBrowserClient(withCsrfHeader: false);

        using var response = await client.PostAsJsonAsync("/api/v1/offers", OfferBody());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_carRental.Requests);
    }

    [Fact]
    public async Task PostOffers_AnonymousWithoutDriverData_Returns400ForAge()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.PostAsJsonAsync("/api/v1/offers", new
        {
            provider = "our",
            carId = "1",
            startDate = Today.AddDays(7),
            endDate = Today.AddDays(10)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("age", out _));
    }

    [Fact]
    public async Task PostOffers_AnonymousWithDriverData_ReturnsOfferInUtcAndSavesQuote()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.PostAsJsonAsync("/api/v1/offers", OfferBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var offer = Assert.Single(json.GetProperty("offers").EnumerateArray());
        Assert.EndsWith("Z", offer.GetProperty("expiresAt").GetString(), StringComparison.Ordinal);
        Assert.Equal(540m, offer.GetProperty("totalPrice").GetDecimal());
        Assert.Equal(1, _factory.WithDb(db => db.Quotes.Count()));
    }

    [Fact]
    public async Task PostOffers_UnknownProvider_Returns404()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.PostAsJsonAsync("/api/v1/offers", OfferBody(provider: "lecturer"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostOffers_ProviderRejects_ReturnsNoOffersWithReason()
    {
        _carRental.Responder = _ => FakeCarRentalHandler.Json(HttpStatusCode.Conflict,
            new { title = "Samochód jest zajęty w wybranym terminie.", status = 409 });
        using var client = _factory.CreateBrowserClient();

        var result = await (await client.PostAsJsonAsync("/api/v1/offers", OfferBody()))
            .Content.ReadFromJsonAsync<QuoteResult>(BrokerApiFactory.Json);

        Assert.NotNull(result);
        Assert.Empty(result.Offers);
        var skipped = Assert.Single(result.SkippedProviders);
        Assert.Equal(SkipReason.Rejected, skipped.Reason);
        Assert.Equal("Samochód jest zajęty w wybranym terminie.", skipped.Message);
    }

    [Fact]
    public async Task PostOffers_LoggedInWithCompleteProfile_FillsDriverDataFromProfile()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");
        var birth = Today.AddYears(-30).AddDays(-1);
        var license = Today.AddYears(-8).AddDays(-1);
        using var put = await client.PutAsJsonAsync("/api/v1/profile", new
        {
            firstName = "Jan",
            lastName = "Kowalski",
            email = "jan@example.com",
            dateOfBirth = birth,
            licenseObtainedOn = license,
            location = "Warszawa"
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        using var response = await client.PostAsJsonAsync("/api/v1/offers", new
        {
            provider = "our",
            carId = "1",
            startDate = Today.AddDays(7),
            endDate = Today.AddDays(10)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = JsonDocument.Parse(_carRental.Requests.Last(r => r.Path == "/api/v1/offers").Body!).RootElement;
        Assert.Equal(30, sent.GetProperty("age").GetInt32());
        Assert.Equal(8, sent.GetProperty("licenseYears").GetInt32());
        Assert.Equal("Warszawa", sent.GetProperty("location").GetString());
        Assert.NotNull(_factory.WithDb(db => db.Quotes.Single().UserProfileId));
    }

    [Fact]
    public async Task GetMe_Anonymous_ReturnsNotAuthenticatedWithDevLoginMode()
    {
        using var client = _factory.CreateBrowserClient();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", BrokerApiFactory.Json);

        Assert.NotNull(me);
        Assert.False(me.IsAuthenticated);
        Assert.Equal(AuthMode.DevLogin, me.AuthMode);
    }

    [Fact]
    public async Task GetMe_AfterDevLoginAsEmployee_ReturnsRoles()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "anna", "Employee");

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", BrokerApiFactory.Json);

        Assert.NotNull(me);
        Assert.True(me.IsAuthenticated);
        Assert.True(me.IsEmployee);
        Assert.Equal("anna@dev.local", me.Email);
        Assert.Contains("Employee", me.Roles);
    }

    [Fact]
    public async Task PostLogout_LoggedIn_ClearsSession()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "anna");

        using var logout = await client.PostAsync("/api/v1/auth/logout", null);
        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/auth/me", BrokerApiFactory.Json);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False(me!.IsAuthenticated);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("//evil.example.com")]
    [InlineData("/\t/evil.example.com")]
    public async Task GetLogin_ExternalReturnUrl_RedirectsToLocalRoot(string returnUrl)
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync($"/api/v1/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.EndsWith("returnUrl=%2F", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProfile_Anonymous_Returns401InsteadOfRedirect()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProfile_FirstVisit_CreatesProfileWithMissingFields()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "ola");

        var profile = await client.GetFromJsonAsync<ProfileResponse>("/api/v1/profile", BrokerApiFactory.Json);

        Assert.NotNull(profile);
        Assert.Equal("ola@dev.local", profile.Email);
        Assert.False(profile.IsComplete);
        Assert.Equal(new[] { "dateOfBirth", "licenseObtainedOn", "location" }, profile.MissingFields);
    }

    [Fact]
    public async Task PutProfile_UnderageDateOfBirth_Returns400()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "ola");

        using var response = await client.PutAsJsonAsync("/api/v1/profile", new
        {
            firstName = "Ola",
            lastName = "Nowak",
            email = "ola@example.com",
            dateOfBirth = Today.AddYears(-17)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeRentals_Anonymous_Returns401()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/api/v1/employee/rentals");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEmployeeRentals_UserWithoutEmployeeRole_Returns403()
    {
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "jan");

        using var response = await client.GetAsync("/api/v1/employee/rentals");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_carRental.Requests);
    }

    [Fact]
    public async Task GetEmployeeRentals_Employee_SendsDevHeadersToCarRentalApi()
    {
        _carRental.Responder = _ => FakeCarRentalHandler.Json(HttpStatusCode.OK, Array.Empty<object>());
        using var client = _factory.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "anna", "Employee");

        using var response = await client.GetAsync("/api/v1/employee/rentals?status=ReturnRequested");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var request = Assert.Single(_carRental.Requests);
        Assert.Equal("/api/v1/employee/rentals?status=ReturnRequested", request.Path);
        Assert.Equal("anna", request.Headers["X-Dev-Employee"]);
        Assert.Equal("Employee", request.Headers["X-Dev-Roles"]);
    }

    [Fact]
    public async Task GetUnknownApiRoute_Returns404ProblemInsteadOfIndexHtml()
    {
        using var client = _factory.CreateBrowserClient();

        using var response = await client.GetAsync("/api/v1/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static object OfferBody(string provider = "our") => new
    {
        provider,
        carId = "1",
        startDate = Today.AddDays(7),
        endDate = Today.AddDays(10),
        age = 30,
        licenseYears = 8,
        location = "Warszawa"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();
}

/// <summary>Każdy klient HTTP dostaje nowy handler, który przekazuje żądania do wspólnego fałszywego API.</summary>
public sealed class ForwardingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override void Dispose(bool disposing)
    {
        // Wspólnego handlera nie zamykamy, bo używają go kolejne klienty w tym samym teście
    }
}
