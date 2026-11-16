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
        Task.FromResult<IReadOnlyList<ICarRentalProvider>>(providers.Where(p => p.IsConfigured).ToList());

    public Task<ICarRentalProvider?> FindAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(providers.FirstOrDefault(p => p.IsConfigured && p.Code == code));

    public Task<IReadOnlyDictionary<string, int>> GetEnabledIdsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(providers.Select((p, i) => (p.Code, Id: i + 1)).ToDictionary(x => x.Code, x => x.Id));
}

public static class TestFactory
{
    public static ProviderAggregator Aggregator(double timeoutSeconds = 15) =>
        new(Microsoft.Extensions.Options.Options.Create(new ProvidersOptions { SearchTimeoutSeconds = timeoutSeconds }),
            NullLogger<ProviderAggregator>.Instance);
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
