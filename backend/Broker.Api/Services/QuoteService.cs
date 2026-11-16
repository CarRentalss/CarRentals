using Broker.Api.Auth;
using Broker.Api.Contracts;
using Broker.Api.Data;
using Broker.Api.Domain;
using Broker.Api.Providers;

namespace Broker.Api.Services;

/// <summary>
/// Wycena. Pyta wypożyczalnię wybranego auta, a równolegle pozostałe aktywne wypożyczalnie o auto tej samej marki
/// i modelu (z katalogu w cache). Całość ma limit 15 s (ProviderAggregator). Oferty zapisujemy w tabeli Quotes.
/// </summary>
public sealed class QuoteService(
    IProviderRegistry registry,
    ProviderAggregator aggregator,
    CatalogService catalog,
    ProfileService profiles,
    ICurrentUser currentUser,
    BrokerDbContext db,
    TimeProvider clock)
{
    private const int MinDriverAge = 18;

    public async Task<ServiceResult<QuoteResult>> CreateAsync(QuoteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // [Required] w kontrakcie gwarantuje te wartości
        var providerCode = request.Provider!.Trim();
        var carId = request.CarId!.Trim();
        var startDate = request.StartDate!.Value;
        var endDate = request.EndDate!.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        if (startDate < today)
        {
            return ServiceError.Validation("startDate", "Data rozpoczęcia nie może być w przeszłości.");
        }

        if (endDate <= startDate)
        {
            return ServiceError.Validation("endDate", "Data zakończenia musi być późniejsza niż data rozpoczęcia.");
        }

        // Dane kierowcy: z żądania, a zalogowanemu brakujące uzupełniamy z profilu (#89, #113)
        var age = request.Age;
        var licenseYears = request.LicenseYears;
        var location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        UserProfile? profile = null;

        if (currentUser.IsAuthenticated)
        {
            var profileResult = await profiles.GetOrCreateCurrentAsync(cancellationToken);
            if (profileResult.IsSuccess)
            {
                profile = profileResult.Value;
                age ??= profile.GetAge(today);
                licenseYears ??= profile.GetLicenseYears(today);
                location ??= string.IsNullOrWhiteSpace(profile.Location) ? null : profile.Location;
            }
        }

        var hint = profile is null ? string.Empty : " albo uzupełnij profil";
        if (age is null)
        {
            return ServiceError.Validation("age", $"Podaj wiek kierowcy{hint}.");
        }

        if (licenseYears is null)
        {
            return ServiceError.Validation("licenseYears", $"Podaj, ile lat masz prawo jazdy{hint}.");
        }

        if (location is null)
        {
            return ServiceError.Validation("location", $"Podaj lokalizację odbioru{hint}.");
        }

        if (age < MinDriverAge)
        {
            return ServiceError.Validation("age", $"Kierowca musi mieć co najmniej {MinDriverAge} lat.");
        }

        if (licenseYears > age - MinDriverAge)
        {
            return ServiceError.Validation("licenseYears", "Staż prawa jazdy nie może być dłuższy niż wiek minus 18 lat.");
        }

        var provider = await registry.FindAsync(providerCode, cancellationToken);
        if (provider is null)
        {
            return ServiceError.NotFound("Nie znaleziono wypożyczalni.");
        }

        var targets = await FindTargetsAsync(provider, carId, location, cancellationToken);

        var result = await aggregator.RunAsync(
            targets.Keys,
            (p, ct) => p.CreateOfferAsync(
                new OfferQuery(targets[p].CarId, age.Value, licenseYears.Value, location, startDate, endDate), ct),
            cancellationToken);

        var providerIds = await registry.GetEnabledIdsAsync(cancellationToken);
        var offers = new List<ProviderOffer>();

        foreach (var success in result.Results)
        {
            if (!providerIds.TryGetValue(success.Provider.Code, out var providerId))
            {
                continue;
            }

            var offer = success.Value;
            var quote = new Quote
            {
                Id = Guid.NewGuid(),
                ProviderId = providerId,
                ProviderOfferId = offer.OfferId,
                ProviderCarId = offer.CarId,
                CarLabel = targets[success.Provider].Label,
                DailyPrice = offer.DailyPrice,
                DailyInsurancePrice = offer.DailyInsurancePrice,
                TotalPrice = offer.TotalPrice,
                Currency = offer.Currency,
                StartDate = offer.StartDate,
                EndDate = offer.EndDate,
                ExpiresAtUtc = offer.ExpiresAtUtc,
                UserProfileId = profile?.Id,
                CreatedAtUtc = now
            };
            db.Quotes.Add(quote);

            offers.Add(new ProviderOffer(
                quote.Id,
                success.Provider.Code,
                success.Provider.DisplayName,
                offer.OfferId,
                offer.CarId,
                quote.CarLabel,
                offer.DailyPrice,
                offer.DailyInsurancePrice,
                offer.TotalPrice,
                offer.Currency,
                offer.StartDate,
                offer.EndDate,
                offer.Days,
                offer.ExpiresAtUtc));
        }

        if (offers.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new QuoteResult(offers.OrderBy(o => o.TotalPrice).ToList(), result.Skipped);
    }

    /// <summary>Które auto u której wypożyczalni wyceniamy: wybrane + podobne u pozostałych (ta sama marka, model, lokalizacja).</summary>
    private async Task<Dictionary<ICarRentalProvider, Target>> FindTargetsAsync(
        ICarRentalProvider selectedProvider, string carId, string location, CancellationToken cancellationToken)
    {
        var selected = catalog.GetCachedCars(selectedProvider.Code)
            .FirstOrDefault(c => string.Equals(c.CarId, carId, StringComparison.Ordinal));

        var targets = new Dictionary<ICarRentalProvider, Target>
        {
            [selectedProvider] = new Target(
                carId,
                selected is null ? $"{selectedProvider.DisplayName}, auto {carId}" : Label(selected))
        };

        // Bez danych auta w cache nie wiemy, czego szukać u innych, więc pytamy tylko wybraną wypożyczalnię
        if (selected is null)
        {
            return targets;
        }

        var active = await registry.GetActiveAsync(cancellationToken);
        foreach (var other in active.Where(p => p.Code != selectedProvider.Code))
        {
            var similar = catalog.GetCachedCars(other.Code)
                .Where(c => c.Available
                    && Same(c.Make, selected.Make)
                    && Same(c.Model, selected.Model)
                    && (c.Location is null || Same(c.Location, location)))
                .OrderByDescending(c => c.Year)
                .FirstOrDefault();

            if (similar is not null)
            {
                targets[other] = new Target(similar.CarId, Label(similar));
            }
        }

        return targets;
    }

    private static string Label(CarListItem car) => $"{car.Make} {car.Model} {car.Year}";

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private sealed record Target(string CarId, string Label);
}
