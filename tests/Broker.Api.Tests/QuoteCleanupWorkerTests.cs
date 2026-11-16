using Broker.Api.Background;
using Broker.Api.Data;
using Broker.Api.Domain;
using Broker.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Api.Tests;

public sealed class QuoteCleanupWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 18, 0, 0, TimeSpan.Zero);
    private readonly SqliteBrokerDb _db = new();

    [Fact]
    public async Task DeleteExpiredAsync_QuotesExpiredLongerThanRetention_DeletesOnlyThose()
    {
        using (var db = _db.CreateContext())
        {
            db.Quotes.AddRange(
                NewQuote(Now.UtcDateTime.AddDays(-2)),     // wygasła 2 dni temu: do usunięcia
                NewQuote(Now.UtcDateTime.AddHours(-1)),    // wygasła godzinę temu: zostaje (retencja 24 h)
                NewQuote(Now.UtcDateTime.AddMinutes(10))); // jeszcze ważna
            await db.SaveChangesAsync();
        }

        var services = new ServiceCollection()
            .AddDbContext<BrokerDbContext>(o => o.UseSqlite(_db.Connection))
            .BuildServiceProvider();
        var worker = new QuoteCleanupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Options.Options.Create(new QuoteCleanupOptions { RetentionHours = 24 }),
            new TestClock(Now),
            NullLogger<QuoteCleanupWorker>.Instance);

        var deleted = await worker.DeleteExpiredAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        using var check = _db.CreateContext();
        Assert.Equal(2, await check.Quotes.CountAsync());
    }

    [Fact]
    public void ProvidersSeed_NewDatabase_HasOurEnabledAndOthersDisabled()
    {
        using var db = _db.CreateContext();

        var providers = db.Providers.OrderBy(p => p.Id).ToList();

        Assert.Equal(new[] { "our", "lecturer", "other-group" }, providers.Select(p => p.Code));
        Assert.Equal(new[] { true, false, false }, providers.Select(p => p.IsEnabled));
    }

    [Fact]
    public async Task UserProfiles_SameEntraObjectIdTwice_RejectedByUniqueIndex()
    {
        using var db = _db.CreateContext();
        db.UserProfiles.Add(NewProfile("oid-1"));
        await db.SaveChangesAsync();

        db.UserProfiles.Add(NewProfile("oid-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static Quote NewQuote(DateTime expiresAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        ProviderId = 1,
        ProviderOfferId = Guid.NewGuid().ToString(),
        ProviderCarId = "1",
        CarLabel = "Toyota Corolla 2022",
        DailyPrice = 150m,
        DailyInsurancePrice = 30m,
        TotalPrice = 540m,
        Currency = "PLN",
        StartDate = new DateOnly(2026, 11, 20),
        EndDate = new DateOnly(2026, 11, 23),
        ExpiresAtUtc = expiresAtUtc,
        CreatedAtUtc = expiresAtUtc.AddMinutes(-10)
    };

    private static UserProfile NewProfile(string objectId) => new()
    {
        Id = Guid.NewGuid(),
        EntraObjectId = objectId,
        Email = "anna@example.com",
        FirstName = "Anna",
        LastName = "Nowak",
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    public void Dispose() => _db.Dispose();
}
