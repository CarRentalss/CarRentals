using Broker.Api.Contracts;
using Broker.Api.Options;
using Broker.Api.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Broker.Api.Services;

/// <summary>Katalog ze wszystkich wypożyczalni z cache (max 30 min).</summary>
public sealed record CatalogSnapshot(IReadOnlyList<CarListItem> Cars, IReadOnlyList<SkippedProvider> Skipped);

/// <summary>
/// Katalog aut ze wszystkich aktywnych wypożyczalni. Katalog każdej wypożyczalni trzymamy w IMemoryCache
/// (wymaganie: najwyżej 30 minut), filtrujemy i stronicujemy w pamięci. Dostępność w terminie sprawdza dopiero wycena.
/// </summary>
public sealed class CatalogService(
    IProviderRegistry registry,
    ProviderAggregator aggregator,
    IMemoryCache cache,
    IOptions<CatalogOptions> options)
{
    public const int DefaultPageSize = 5;

    public static string CacheKey(string providerCode) => $"catalog:{providerCode}";

    /// <summary>Wszystkie auta: z cache, a brakujące katalogi pobiera równolegle (limit 15 s).</summary>
    public async Task<CatalogSnapshot> GetAllCarsAsync(CancellationToken cancellationToken)
    {
        var providers = await registry.GetActiveAsync(cancellationToken);
        var cars = new List<CarListItem>();
        var toFetch = new List<ICarRentalProvider>();

        foreach (var provider in providers)
        {
            if (cache.TryGetValue(CacheKey(provider.Code), out IReadOnlyList<CarListItem>? cached) && cached is not null)
            {
                cars.AddRange(cached);
            }
            else
            {
                toFetch.Add(provider);
            }
        }

        IReadOnlyList<SkippedProvider> skipped = [];
        if (toFetch.Count > 0)
        {
            var result = await aggregator.RunAsync(toFetch, (p, ct) => p.GetCarsAsync(ct), cancellationToken);
            foreach (var success in result.Results)
            {
                var items = success.Value.Select(car => ToListItem(success.Provider, car)).ToList();
                // Błędów nie cache'ujemy, żeby następne żądanie spróbowało jeszcze raz
                cache.Set<IReadOnlyList<CarListItem>>(CacheKey(success.Provider.Code), items, TimeSpan.FromMinutes(options.Value.CacheMinutes));
                cars.AddRange(items);
            }

            skipped = result.Skipped;
        }

        return new CatalogSnapshot(cars, skipped);
    }

    /// <summary>Auta danej wypożyczalni, tylko jeśli są już w cache (bez wołania API).</summary>
    public IReadOnlyList<CarListItem> GetCachedCars(string providerCode) =>
        cache.TryGetValue(CacheKey(providerCode), out IReadOnlyList<CarListItem>? cached) && cached is not null ? cached : [];

    public async Task<PagedResult<CarListItem>> SearchAsync(CarSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var snapshot = await GetAllCarsAsync(cancellationToken);

        var filtered = snapshot.Cars
            .Where(c => Matches(c.Make, query.Make))
            .Where(c => Matches(c.Model, query.Model))
            .Where(c => query.Year is null || c.Year == query.Year)
            .Where(c => Matches(c.Location, query.Location))
            .OrderBy(c => c.Make, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Model, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(c => c.Year)
            .ThenBy(c => c.Provider, StringComparer.Ordinal)
            .ThenBy(c => c.CarId, StringComparer.Ordinal)
            .ToList();

        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize > 0 ? query.PageSize : DefaultPageSize;
        var totalCount = filtered.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = filtered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<CarListItem>(items, page, pageSize, totalCount, totalPages)
        {
            SkippedProviders = snapshot.Skipped
        };
    }

    /// <summary>Marki i modele do strony głównej (z katalogu, więc zmieniają się razem z API wypożyczalni).</summary>
    public async Task<IReadOnlyList<MakeModels>> GetMakesAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetAllCarsAsync(cancellationToken);

        return snapshot.Cars
            .GroupBy(c => c.Make.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new MakeModels(
                g.First().Make.Trim(),
                g.Select(c => c.Model.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    /// <summary>Szczegóły auta prosto od wypożyczalni (aktualna dostępność).</summary>
    public async Task<ServiceResult<CarDetails>> GetCarAsync(string providerCode, string carId, CancellationToken cancellationToken)
    {
        var provider = await registry.FindAsync(providerCode, cancellationToken);
        if (provider is null)
        {
            return ServiceError.NotFound("Nie znaleziono wypożyczalni.");
        }

        ProviderCar? car;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(aggregator.Timeout);
            car = await provider.GetCarAsync(carId, timeout.Token);
        }
        catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return ServiceError.Unavailable($"Wypożyczalnia {provider.DisplayName} chwilowo nie odpowiada.");
        }

        if (car is null)
        {
            return ServiceError.NotFound("Nie znaleziono samochodu.");
        }

        return new CarDetails(provider.Code, provider.DisplayName, car.CarId, car.Make, car.Model, car.Year, car.Location, car.Available,
            SafeImageUrl(car.ImageUrl), car.ImageCredit);
    }

    private static CarListItem ToListItem(ICarRentalProvider provider, ProviderCar car) =>
        new(provider.Code, provider.DisplayName, car.CarId, car.Make, car.Model, car.Year, car.Location, car.Available,
            SafeImageUrl(car.ImageUrl), car.ImageCredit);

    /// <summary>Do frontu przepuszczamy tylko pełne adresy https (zdjęcie od wypożyczalni idzie prosto do img src).</summary>
    internal static string? SafeImageUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri.ToString() : null;

    private static bool Matches(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || string.Equals(value?.Trim(), filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
