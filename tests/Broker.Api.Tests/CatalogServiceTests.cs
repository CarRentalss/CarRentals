using Broker.Api.Contracts;
using Broker.Api.Options;
using Broker.Api.Providers;
using Broker.Api.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Broker.Api.Tests;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private static FakeProvider OurProviderWith12Cars()
    {
        var provider = new FakeProvider("our", "Car Rentals");
        provider.Cars.AddRange(
        [
            FakeProvider.Car(1, "Toyota", "Corolla", 2022),
            FakeProvider.Car(2, "Toyota", "Yaris", 2023),
            FakeProvider.Car(3, "Skoda", "Octavia", 2021),
            FakeProvider.Car(4, "Volkswagen", "Golf", 2022, "Kraków"),
            FakeProvider.Car(5, "Kia", "Sportage", 2023, "Kraków"),
            FakeProvider.Car(6, "Hyundai", "i30", 2020, "Kraków"),
            FakeProvider.Car(7, "Ford", "Focus", 2021, "Gdańsk"),
            FakeProvider.Car(8, "Renault", "Clio", 2022, "Gdańsk"),
            FakeProvider.Car(9, "BMW", "Seria 3", 2023),
            FakeProvider.Car(10, "Audi", "A4", 2022, "Wrocław"),
            FakeProvider.Car(11, "Toyota", "RAV4", 2024, "Wrocław"),
            FakeProvider.Car(12, "Opel", "Corsa", 2021, "Poznań")
        ]);
        return provider;
    }

    private CatalogService CreateService(params ICarRentalProvider[] providers) =>
        new(new FakeRegistry(providers), TestFactory.Aggregator(timeoutSeconds: 0.5), _cache,
            Microsoft.Extensions.Options.Options.Create(new CatalogOptions { CacheMinutes = 10 }));

    [Fact]
    public async Task SearchAsync_NoFilters_ReturnsFirstPageOfFive()
    {
        var service = CreateService(OurProviderWith12Cars());

        var page = await service.SearchAsync(new CarSearchQuery(), CancellationToken.None);

        Assert.Equal(5, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(5, page.PageSize);
        Assert.Equal(12, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Empty(page.SkippedProviders);
    }

    [Fact]
    public async Task SearchAsync_LastPage_ReturnsRemainingCars()
    {
        var service = CreateService(OurProviderWith12Cars());

        var page = await service.SearchAsync(new CarSearchQuery { Page = 3, PageSize = 5 }, CancellationToken.None);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_PageAfterLast_ReturnsEmptyItemsWithTotals()
    {
        var service = CreateService(OurProviderWith12Cars());

        var page = await service.SearchAsync(new CarSearchQuery { Page = 4 }, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(12, page.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_AllPages_ReturnEveryCarOnce()
    {
        var service = CreateService(OurProviderWith12Cars());
        var ids = new List<string>();

        for (var pageNumber = 1; pageNumber <= 3; pageNumber++)
        {
            var page = await service.SearchAsync(new CarSearchQuery { Page = pageNumber }, CancellationToken.None);
            ids.AddRange(page.Items.Select(c => c.CarId));
        }

        Assert.Equal(12, ids.Distinct().Count());
    }

    [Fact]
    public async Task SearchAsync_FilterByMakeIgnoringCase_ReturnsOnlyThatMakeSortedByModel()
    {
        var service = CreateService(OurProviderWith12Cars());

        var page = await service.SearchAsync(new CarSearchQuery { Make = "toyota" }, CancellationToken.None);

        Assert.Equal(3, page.TotalCount);
        Assert.All(page.Items, c => Assert.Equal("Toyota", c.Make));
        Assert.Equal(new[] { "Corolla", "RAV4", "Yaris" }, page.Items.Select(c => c.Model));
    }

    [Fact]
    public async Task SearchAsync_FilterByYearAndLocation_ReturnsMatchingCars()
    {
        var service = CreateService(OurProviderWith12Cars());

        var page = await service.SearchAsync(new CarSearchQuery { Year = 2022, Location = "warszawa" }, CancellationToken.None);

        var car = Assert.Single(page.Items);
        Assert.Equal("Corolla", car.Model);
        Assert.Equal("our", car.Provider);
        Assert.Equal("Car Rentals", car.ProviderName);
    }

    [Fact]
    public async Task SearchAsync_SecondCall_UsesCacheInsteadOfProvider()
    {
        var provider = OurProviderWith12Cars();
        var service = CreateService(provider);

        await service.SearchAsync(new CarSearchQuery(), CancellationToken.None);
        await service.SearchAsync(new CarSearchQuery { Make = "Kia" }, CancellationToken.None);
        await service.GetMakesAsync(CancellationToken.None);

        Assert.Equal(1, provider.GetCarsCalls);
    }

    [Fact]
    public async Task SearchAsync_ProviderFails_ReturnsOtherProvidersAndDoesNotCacheFailure()
    {
        var broken = new FakeProvider("other-group") { Error = new HttpRequestException("500") };
        var service = CreateService(OurProviderWith12Cars(), broken);

        var page = await service.SearchAsync(new CarSearchQuery(), CancellationToken.None);
        await service.SearchAsync(new CarSearchQuery(), CancellationToken.None);

        Assert.Equal(12, page.TotalCount);
        var skipped = Assert.Single(page.SkippedProviders);
        Assert.Equal("other-group", skipped.Provider);
        Assert.Equal(SkipReason.Error, skipped.Reason);
        Assert.Equal(2, broken.GetCarsCalls);
    }

    [Fact]
    public async Task SearchAsync_TwoProviders_MergesCatalogs()
    {
        var other = new FakeProvider("other-group");
        other.Cars.Add(FakeProvider.Car(100, "Toyota", "Corolla", 2024));
        var service = CreateService(OurProviderWith12Cars(), other);

        var page = await service.SearchAsync(new CarSearchQuery { Make = "Toyota", Model = "Corolla" }, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        // Nowszy rocznik pierwszy
        Assert.Equal(new[] { "other-group", "our" }, page.Items.Select(c => c.Provider));
    }

    [Fact]
    public async Task GetMakesAsync_CatalogWithDuplicates_ReturnsSortedDistinctModels()
    {
        var other = new FakeProvider("other-group");
        other.Cars.Add(FakeProvider.Car(100, "toyota", "Corolla", 2024));
        var service = CreateService(OurProviderWith12Cars(), other);

        var makes = await service.GetMakesAsync(CancellationToken.None);

        Assert.Equal(10, makes.Count);
        Assert.Equal("Audi", makes[0].Make);
        var toyota = Assert.Single(makes, m => m.Make == "Toyota");
        Assert.Equal(new[] { "Corolla", "RAV4", "Yaris" }, toyota.Models);
    }

    [Fact]
    public async Task GetCarAsync_UnknownProvider_ReturnsNotFound()
    {
        var service = CreateService(OurProviderWith12Cars());

        var result = await service.GetCarAsync("lecturer", "1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task GetCarAsync_ProviderDown_ReturnsUnavailable()
    {
        var broken = new FakeProvider("our") { Error = new HttpRequestException("connection refused") };
        var service = CreateService(broken);

        var result = await service.GetCarAsync("our", "1", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorType.Unavailable, result.Error.Type);
    }

    public void Dispose() => _cache.Dispose();
}
