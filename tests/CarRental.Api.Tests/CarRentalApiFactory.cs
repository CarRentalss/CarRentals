using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarRental.Api.Auth;
using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Email;
using CarRental.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CarRental.Api.Tests;

/// <summary>Zegar, którym w testach sterujemy ręcznie (np. żeby oferta wygasła).</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Uruchamia całe API w pamięci, z bazą SQLite w pamięci zamiast SQL Servera.
/// Każdy test tworzy własną fabrykę, więc testy nie widzą swoich danych.
/// </summary>
public sealed class CarRentalApiFactory : WebApplicationFactory<Program>
{
    public const string OurBrokerKey = "test-our-broker-key-0001";
    public const string OtherBrokerKey = "test-other-broker-key-0002";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CarRentalApiFactory()
    {
        // Baza w pamięci żyje tak długo, jak otwarte jest połączenie
        _connection.Open();
    }

    /// <summary>"Dziś" w testach: poniedziałek 2.11.2026, 9:00 UTC.</summary>
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    public string StoragePath { get; } = Path.Combine(Path.GetTempPath(), "carrental-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Maile "wysłane" w teście (zamiast ACS i LoggingEmailSender).</summary>
    public FakeEmailSender Emails { get; } = new();

    /// <summary>Dodatkowe ustawienia dla jednego testu (np. Images:BaseUrl).</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Podmiana serwisów dla jednego testu (np. fałszywy Blob albo kolejka).</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

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
                ["RentalProcessing:Enabled"] = "false",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Storage:LocalPath"] = StoragePath
            });
            config.AddInMemoryCollection(Settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<CarRentalDbContext>(options => options.UseSqlite(_connection));
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IEmailSender>(Emails);
            ConfigureServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CarRentalDbContext>().Database.EnsureCreated();

        return host;
    }

    public HttpClient CreateBrokerClient(string apiKey = OurBrokerKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.ApiKeyHeader, apiKey);
        return client;
    }

    public HttpClient CreateEmployeeClient(string roles = AuthConstants.EmployeeRole)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(AuthConstants.DevEmployeeHeader, "anna.pracownik");
        client.DefaultRequestHeaders.Add(AuthConstants.DevRolesHeader, roles);
        return client;
    }

    /// <summary>Uruchamia to, co normalnie robi proces w tle: potwierdza czekające wnioski.</summary>
    public async Task ConfirmPendingRentalsAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RentalConfirmationProcessor>().ProcessPendingAsync(TimeSpan.Zero, CancellationToken.None);
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
        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }
}

/// <summary>Zapamiętuje maile zamiast je wysyłać.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Wspólne kroki używane w wielu testach.</summary>
public static class TestApi
{
    public static readonly DateOnly Start = new(2026, 11, 10);
    public static readonly DateOnly End = new(2026, 11, 13);

    public static object OfferBody(int carId = 1, int age = 30, int licenseYears = 10, string location = "Warszawa",
        DateOnly? start = null, DateOnly? end = null) => new
        {
            carId,
            age,
            licenseYears,
            location,
            startDate = start ?? Start,
            endDate = end ?? End
        };

    public static async Task<OfferResponse> CreateOfferAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/offers", body ?? OfferBody());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OfferResponse>(CarRentalApiFactory.Json))!;
    }

    public static object RentalBody(Guid offerId, string email = "jan.kowalski@example.com") => new
    {
        offerId,
        firstName = "Jan",
        lastName = "Kowalski",
        email
    };

    public static async Task<RentalStatusChangedResponse> CreateRentalAsync(HttpClient client, Guid offerId, string email = "jan.kowalski@example.com")
    {
        var response = await client.PostAsJsonAsync("/api/v1/rentals", RentalBody(offerId, email));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RentalStatusChangedResponse>(CarRentalApiFactory.Json))!;
    }

    /// <summary>Oferta, wniosek i potwierdzenie w tle, czyli gotowy aktywny wynajem.</summary>
    public static async Task<Guid> CreateConfirmedRentalAsync(CarRentalApiFactory factory, HttpClient broker)
    {
        var offer = await CreateOfferAsync(broker);
        var rental = await CreateRentalAsync(broker, offer.OfferId);
        await factory.ConfirmPendingRentalsAsync();
        return rental.RentalId;
    }
}
