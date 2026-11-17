using System.Diagnostics;
using Broker.Api.Contracts;
using Broker.Api.Providers;

namespace Broker.Api.Tests;

public sealed class ProviderAggregatorTests
{
    private static readonly OfferQuery Query = new("1", 30, 8, "Warszawa", new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 4));

    [Fact]
    public async Task RunAsync_AllProvidersRespond_ReturnsOfferFromEach()
    {
        var aggregator = TestFactory.Aggregator();
        ICarRentalProvider[] providers = [new FakeProvider("our"), new FakeProvider("other-group")];

        var result = await aggregator.RunAsync(providers, (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task RunAsync_ProviderSlowerThanLimit_SkipsItAndReturnsOthers()
    {
        var aggregator = TestFactory.Aggregator(timeoutSeconds: 0.3);
        var fast = new FakeProvider("our");
        var slow = new FakeProvider("lecturer", "Wypożyczalnia prowadzącego") { Delay = TimeSpan.FromSeconds(10) };

        var stopwatch = Stopwatch.StartNew();
        var result = await aggregator.RunAsync([fast, slow], (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);
        stopwatch.Stop();

        var offer = Assert.Single(result.Results);
        Assert.Equal("our", offer.Provider.Code);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("lecturer", skipped.Provider);
        Assert.Equal("Wypożyczalnia prowadzącego", skipped.ProviderName);
        Assert.Equal(SkipReason.Timeout, skipped.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Czekaliśmy {stopwatch.Elapsed}, a limit to 0,3 s.");
    }

    [Fact]
    public async Task RunAsync_ProviderIgnoresCancellation_StillFinishesAtLimit()
    {
        var aggregator = TestFactory.Aggregator(timeoutSeconds: 0.3);
        var hanging = new FakeProvider("other-group") { Delay = TimeSpan.FromSeconds(10), IgnoreCancellation = true };

        var stopwatch = Stopwatch.StartNew();
        var result = await aggregator.RunAsync([hanging], (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);
        stopwatch.Stop();

        Assert.Empty(result.Results);
        Assert.Equal(SkipReason.Timeout, Assert.Single(result.Skipped).Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Czekaliśmy {stopwatch.Elapsed}, a limit to 0,3 s.");
    }

    [Fact]
    public void RunAsync_DefaultSettings_UsesFifteenSecondLimit()
    {
        var aggregator = TestFactory.Aggregator();

        Assert.Equal(TimeSpan.FromSeconds(15), aggregator.Timeout);
        Assert.Equal(15, new ProvidersOptions().SearchTimeoutSeconds);
    }

    [Fact]
    public async Task RunAsync_ProviderThrows_SkipsItWithErrorReason()
    {
        var aggregator = TestFactory.Aggregator();
        var broken = new FakeProvider("our") { Error = new HttpRequestException("500") };
        var working = new FakeProvider("other-group");

        var result = await aggregator.RunAsync([broken, working], (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);

        Assert.Equal("other-group", Assert.Single(result.Results).Provider.Code);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal("our", skipped.Provider);
        Assert.Equal(SkipReason.Error, skipped.Reason);
    }

    [Fact]
    public async Task RunAsync_ProviderRejectsOffer_SkipsItWithProviderMessage()
    {
        var aggregator = TestFactory.Aggregator();
        var provider = new FakeProvider("our") { Error = new ProviderRejectedException("Samochód jest zajęty w wybranym terminie.", 409) };

        var result = await aggregator.RunAsync([provider], (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);

        Assert.Empty(result.Results);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(SkipReason.Rejected, skipped.Reason);
        Assert.Equal("Samochód jest zajęty w wybranym terminie.", skipped.Message);
    }

    [Fact]
    public async Task RunAsync_AllProvidersFail_ReturnsNoOffersInsteadOfThrowing()
    {
        var aggregator = TestFactory.Aggregator(timeoutSeconds: 0.3);
        ICarRentalProvider[] providers =
        [
            new FakeProvider("our") { Error = new InvalidOperationException("boom") },
            new FakeProvider("lecturer") { Delay = TimeSpan.FromSeconds(10) },
            new FakeProvider("other-group") { Error = new ProviderRejectedException("Brak aut w tej lokalizacji.", 400) }
        ];

        var result = await aggregator.RunAsync(providers, (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.Equal(3, result.Skipped.Count);
        Assert.Equal(
            new[] { SkipReason.Error, SkipReason.Timeout, SkipReason.Rejected },
            result.Skipped.Select(s => s.Reason));
    }

    [Fact]
    public async Task RunAsync_NoProviders_ReturnsEmptyResult()
    {
        var aggregator = TestFactory.Aggregator();

        var result = await aggregator.RunAsync([], (p, ct) => p.CreateOfferAsync(Query, ct), CancellationToken.None);

        Assert.Empty(result.Results);
        Assert.Empty(result.Skipped);
    }

    [Fact]
    public async Task RunAsync_CallerCancels_ThrowsOperationCanceled()
    {
        var aggregator = TestFactory.Aggregator();
        var slow = new FakeProvider("our") { Delay = TimeSpan.FromSeconds(10) };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            aggregator.RunAsync([slow], (p, ct) => p.CreateOfferAsync(Query, ct), cancellation.Token));
    }
}
