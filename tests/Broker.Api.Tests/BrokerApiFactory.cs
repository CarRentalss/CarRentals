using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Broker.Api.Auth;
using Broker.Api.Data;
using Broker.Api.Providers.OurCarRental;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Broker.Api.Tests;

/// <summary>
/// Uruchamia Brokera w pamięci: Broker DB na SQLite, logowanie dev-login, a Car Rental API
/// podstawione handlerem HTTP (fałszywym albo TestServerem prawdziwego Car Rental API).
/// </summary>
public sealed class BrokerApiFactory(Func<HttpMessageHandler> carRentalHandler) : WebApplicationFactory<Program>
{
    public const string CarRentalBaseUrl = "http://carrental.test/";
    public const string OurBrokerKey = "test-our-broker-key-0001";

    private readonly SqliteConnection _connection = OpenConnection();

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Program.cs czyta tryb logowania jeszcze przed Build(), a ConfigureAppConfiguration działa dopiero przy Build().
        // UseSetting idzie do aplikacji jako argumenty startowe, więc Program widzi te wartości od razu.
        builder.UseSetting("Auth:UseDevLogin", "true");
        builder.UseSetting("AzureAd:ClientId", string.Empty);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:Our:BaseUrl"] = CarRentalBaseUrl,
                ["Providers:Our:ApiKey"] = OurBrokerKey,
                ["Providers:SearchTimeoutSeconds"] = "15",
                ["Auth:UseDevLogin"] = "true",
                ["AzureAd:ClientId"] = "",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["Quotes:CleanupEnabled"] = "false",
                ["Swagger:Enabled"] = "false"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<BrokerDbContext>(options => options.UseSqlite(_connection));
            services.AddHttpClient<OurCarRentalClient>().ConfigurePrimaryHttpMessageHandler(carRentalHandler);
            services.AddHttpClient<OurEmployeeClient>().ConfigurePrimaryHttpMessageHandler(carRentalHandler);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BrokerDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>Klient przeglądarki: https (ciasteczko poza Development ma Secure), ciasteczka i nagłówek X-CSRF.</summary>
    public HttpClient CreateBrowserClient(bool withCsrfHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        if (withCsrfHeader)
        {
            client.DefaultRequestHeaders.Add(CsrfHeaderFilter.HeaderName, CsrfHeaderFilter.HeaderValue);
        }

        return client;
    }

    /// <summary>Loguje przez dev-login (ustawia ciasteczko w kliencie).</summary>
    public static async Task LoginAsync(HttpClient client, string name, string? role = null)
    {
        var url = $"/api/v1/auth/dev-login?name={name}" + (role is null ? string.Empty : $"&role={role}");
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public T WithDb<T>(Func<BrokerDbContext, T> action)
    {
        using var scope = Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<BrokerDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static SqliteConnection OpenConnection()
    {
        // Baza w pamięci żyje tak długo, jak otwarte jest połączenie
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }
}
