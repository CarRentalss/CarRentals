using Broker.Api.Contracts;
using Broker.Api.Data;
using Broker.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Broker.Api.Tests;

/// <summary>Wypożyczalnia do testów: można ustawić opóźnienie, błąd i odpowiedzi.</summary>
public sealed class FakeProvider(string code, string displayName = "") : ICarRentalProvider
{
    public string Code { get; } = code;
    public string DisplayName { get; } = string.IsNullOrEmpty(displayName) ? code : displayName;
    public bool IsEnabled { get; set; } = true;
    public bool IsConfigured { get; set; } = true;

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Opóźnienie bez reagowania na anulowanie (dostawca, który "wisi").</summary>
    public bool IgnoreCancellation { get; set; }

    public Exception? Error { get; set; }
    public List<ProviderCar> Cars { get; } = [];
    public int GetCarsCalls { get; private set; }
    public List<OfferQuery> OfferQueries { get; } = [];

    public async Task<IReadOnlyList<ProviderCar>> GetCarsAsync(CancellationToken cancellationToken)
    {
        GetCarsCalls++;
        await SimulateAsync(cancellationToken);
        return Cars.ToList();
    }

    public async Task<ProviderCar?> GetCarAsync(string carId, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return Cars.FirstOrDefault(c => c.CarId == carId);
    }

    public async Task<ProviderQuote> CreateOfferAsync(OfferQuery query, CancellationToken cancellationToken)
    {
        OfferQueries.Add(query);
        await SimulateAsync(cancellationToken);
        var days = query.EndDate.DayNumber - query.StartDate.DayNumber;
        return new ProviderQuote($"{Code}-offer", query.CarId, 100m, 20m, 120m * days, "PLN",
            query.StartDate, query.EndDate, days, DateTime.UtcNow.AddMinutes(10));
    }

    public async Task<ProviderRental> CreateRentalAsync(RentalOrder order, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return new ProviderRental($"{Code}-rental", RentalStatus.Pending);
    }

    public async Task<ProviderRental?> GetRentalAsync(string rentalId, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return new ProviderRental(rentalId, RentalStatus.Confirmed);
    }

    public async Task<ProviderRental> RequestReturnAsync(string rentalId, string email, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return new ProviderRental(rentalId, RentalStatus.ReturnRequested);
    }

    private async Task SimulateAsync(CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, IgnoreCancellation ? CancellationToken.None : cancellationToken);
        }

        if (Error is not null)
        {
            throw Error;
        }
    }

    public static ProviderCar Car(int id, string make, string model, int year, string location = "Warszawa") =>
        new(id.ToString(System.Globalization.CultureInfo.InvariantCulture), make, model, year, location, true);
}

/// <summary>Rejestr z listą dostawców podaną w teście (bez bazy).</summary>
public sealed class FakeRegistry(params ICarRentalProvider[] providers) : IProviderRegistry
{
    public Task<IReadOnlyList<ICarRentalProvider>> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ICarRentalProvider>>(providers.Where(p => p.IsEnabled && p.IsConfigured).ToList());

    public Task<ICarRentalProvider?> FindAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(providers.FirstOrDefault(p => p.IsEnabled && p.IsConfigured && p.Code == code));

    public Task<IReadOnlyDictionary<string, int>> GetEnabledIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(providers.Select((p, i) => (p.Code, Id: i + 1)).ToDictionary(x => x.Code, x => x.Id));
}

public static class TestFactory
{
    public static ProviderAggregator Aggregator(double timeoutSeconds = 15) =>
        new(Microsoft.Extensions.Options.Options.Create(new ProvidersOptions { SearchTimeoutSeconds = timeoutSeconds }),
            NullLogger<ProviderAggregator>.Instance);
}

/// <summary>
/// Handler HTTP do testów dostawców: odpowiedź z funkcji, zapis żądań (metoda, adres, nagłówki, body).
/// Opóźnienie respektuje anulowanie, tak jak prawdziwe połączenie.
/// </summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string? Body)> Requests { get; } = [];

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (Requests)
        {
            Requests.Add((request.Method, request.RequestUri!, headers, body));
        }

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        return respond(request, body);
    }

    public static HttpResponseMessage Json(System.Net.HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>IHttpClientFactory, który zawsze daje klienta z tym samym handlerem.</summary>
public sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Zegar ustawiany ręcznie.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Broker DB na SQLite w pamięci (żyje, dopóki połączenie jest otwarte).</summary>
public sealed class SqliteBrokerDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteBrokerDb()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<BrokerDbContext>().UseSqlite(_connection).Options;
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public DbContextOptions<BrokerDbContext> Options { get; }

    public SqliteConnection Connection => _connection;

    public BrokerDbContext CreateContext() => new(Options);

    public void Dispose() => _connection.Dispose();
}
