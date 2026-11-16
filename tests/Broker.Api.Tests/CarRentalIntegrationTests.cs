extern alias CarRentalApi;

using System.Net;
using System.Net.Http.Json;
using Broker.Api.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CarRentalDbContext = CarRentalApi::CarRental.Api.Data.CarRentalDbContext;
using CarRentalProgram = CarRentalApi::Program;

namespace Broker.Api.Tests;

/// <summary>
/// Car Rental API uruchomione w pamięci (prawdziwy kod, baza SQLite), tak jak w tests/CarRental.Api.Tests.
/// Broker woła je przez handler TestServera, więc idzie prawdziwe HTTP z x-api-key.
/// </summary>
public sealed class CarRentalApiTestFactory : WebApplicationFactory<CarRentalProgram>
{
    public const string OurBrokerKey = BrokerApiFactory.OurBrokerKey;
    public const string OtherBrokerKey = "test-other-broker-key-0002";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "broker-tests", Guid.NewGuid().ToString("N"));

    public CarRentalApiTestFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ApiKeys:Clients:0:Name"] = "OurBroker",
                ["ApiKeys:Clients:0:Key"] = OurBrokerKey,
                ["ApiKeys:Clients:1:Name"] = "OtherGroupBroker",
                ["ApiKeys:Clients:1:Key"] = OtherBrokerKey,
                ["AzureAd:ClientId"] = "",
                ["RentalProcessing:Enabled"] = "false",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Storage:LocalPath"] = _storagePath
            });
        });

        builder.ConfigureTestServices(services =>
            services.AddDbContext<CarRentalDbContext>(options => options.UseSqlite(_connection)));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CarRentalDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>Klient "jak broker" bezpośrednio do Car Rental API (do przygotowania danych w teście).</summary>
    public HttpClient CreateBrokerClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("x-api-key", OurBrokerKey);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Cleanup();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Cleanup();
        GC.SuppressFinalize(this);
    }

    private void Cleanup()
    {
        _connection.Dispose();
        if (Directory.Exists(_storagePath))
        {
            Directory.Delete(_storagePath, recursive: true);
        }
    }
}

/// <summary>Testy integracyjne Brokera z naszym Car Rental API (#84: "Dodać testy integracyjne Broker z Car Rental API").</summary>
public sealed class CarRentalIntegrationTests : IAsyncLifetime
{
    private readonly CarRentalApiTestFactory _carRental = new();
    private BrokerApiFactory _broker = null!;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public Task InitializeAsync()
    {
        // Najpierw startujemy Car Rental API, potem Broker dostaje handler do jego TestServera
        _ = _carRental.Server;
        _broker = new BrokerApiFactory(() => _carRental.Server.CreateHandler());
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetCars_ThroughBroker_ReturnsSeededCarsFromCarRentalApi()
    {
        using var client = _broker.CreateBrowserClient();

        var page = await client.GetFromJsonAsync<PagedResult<CarListItem>>("/api/v1/cars?make=Toyota&pageSize=10", BrokerApiFactory.Json);
        var all = await client.GetFromJsonAsync<PagedResult<CarListItem>>("/api/v1/cars", BrokerApiFactory.Json);

        Assert.NotNull(page);
        Assert.NotNull(all);
        Assert.Equal(12, all.TotalCount);
        Assert.Equal(3, all.TotalPages);
        Assert.Equal(5, all.Items.Count);
        Assert.Empty(all.SkippedProviders);
        Assert.Equal(new[] { "Corolla", "RAV4", "Yaris" }, page.Items.Select(c => c.Model));
        Assert.All(page.Items, c => Assert.Equal("our", c.Provider));
    }

    [Fact]
    public async Task GetCarDetails_ExistingAndMissingCar_MapsCarRentalApiResponses()
    {
        using var client = _broker.CreateBrowserClient();

        var car = await client.GetFromJsonAsync<CarDetails>("/api/v1/cars/our/1", BrokerApiFactory.Json);
        using var missing = await client.GetAsync("/api/v1/cars/our/999");

        Assert.NotNull(car);
        Assert.Equal("Toyota", car.Make);
        Assert.Equal("Corolla", car.Model);
        Assert.Equal("Warszawa", car.Location);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task PostOffers_ValidRequest_ReturnsOfferFromCarRentalApiAndSavesQuote()
    {
        using var client = _broker.CreateBrowserClient();
        // Katalog w cache, żeby wycena miała opis auta
        await client.GetAsync("/api/v1/cars");

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
        Assert.NotNull(result);
        Assert.Empty(result.SkippedProviders);
        var offer = Assert.Single(result.Offers);
        Assert.Equal("our", offer.Provider);
        Assert.Equal("Toyota Corolla 2022", offer.CarLabel);
        Assert.Equal(3, offer.Days);
        Assert.True(offer.DailyPrice > 0);
        Assert.True(offer.DailyInsurancePrice > 0);
        // Wymaganie: oferta ważna co najmniej 10 minut
        Assert.True(offer.ExpiresAt >= DateTime.UtcNow.AddMinutes(9));

        var saved = _broker.WithDb(db => db.Quotes.Single());
        Assert.Equal(offer.QuoteId, saved.Id);
        Assert.Equal(offer.OfferId, saved.ProviderOfferId);
    }

    [Fact]
    public async Task PostOffers_WrongLocation_ReturnsNoOffersWithCarRentalApiMessage()
    {
        using var client = _broker.CreateBrowserClient();

        var result = await (await client.PostAsJsonAsync("/api/v1/offers", new
        {
            provider = "our",
            carId = "1",
            startDate = Today.AddDays(7),
            endDate = Today.AddDays(10),
            age = 30,
            licenseYears = 8,
            location = "Gdańsk"
        })).Content.ReadFromJsonAsync<QuoteResult>(BrokerApiFactory.Json);

        Assert.NotNull(result);
        Assert.Empty(result.Offers);
        var skipped = Assert.Single(result.SkippedProviders);
        Assert.Equal(SkipReason.Rejected, skipped.Reason);
        Assert.Contains("Warszawa", skipped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEmployeeRentals_EmployeeViaBroker_ReturnsRentalsFromCarRentalApi()
    {
        // Wynajem złożony bezpośrednio w Car Rental API (oferta + wniosek)
        using var carRentalClient = _carRental.CreateBrokerClient();
        var offerResponse = await carRentalClient.PostAsJsonAsync("/api/v1/offers", new
        {
            carId = 2,
            age = 35,
            licenseYears = 10,
            location = "Warszawa",
            startDate = Today.AddDays(3),
            endDate = Today.AddDays(5)
        });
        offerResponse.EnsureSuccessStatusCode();
        var offer = await offerResponse.Content.ReadFromJsonAsync<OfferIdOnly>(BrokerApiFactory.Json);
        var rentalResponse = await carRentalClient.PostAsJsonAsync("/api/v1/rentals", new
        {
            offerId = offer!.OfferId,
            firstName = "Jan",
            lastName = "Kowalski",
            email = "jan.kowalski@example.com"
        });
        rentalResponse.EnsureSuccessStatusCode();
        var rental = await rentalResponse.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(BrokerApiFactory.Json);

        using var client = _broker.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "anna", "Employee");

        var list = await client.GetFromJsonAsync<List<EmployeeRentalListItem>>("/api/v1/employee/rentals?status=Pending", BrokerApiFactory.Json);
        var details = await client.GetFromJsonAsync<EmployeeRentalDetails>($"/api/v1/employee/rentals/{rental!.RentalId}", BrokerApiFactory.Json);

        var item = Assert.Single(list!);
        Assert.Equal(rental.RentalId, item.RentalId);
        Assert.Equal(RentalStatus.Pending, item.Status);
        Assert.Equal("Jan Kowalski", item.CustomerName);
        Assert.NotNull(details);
        Assert.Equal("OurBroker", details.BrokerName);
    }

    [Fact]
    public async Task GetEmployeeRentals_UnknownRental_Returns404FromCarRentalApi()
    {
        using var client = _broker.CreateBrowserClient();
        await BrokerApiFactory.LoginAsync(client, "anna", "Employee");

        using var response = await client.GetAsync($"/api/v1/employee/rentals/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        await _broker.DisposeAsync();
        await _carRental.DisposeAsync();
    }

    private sealed record OfferIdOnly(Guid OfferId);
}
