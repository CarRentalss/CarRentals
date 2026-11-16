using Broker.Api.Contracts;
using Broker.Api.Data;
using Broker.Api.Domain;
using Broker.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace Broker.Api.Services;

/// <summary>
/// Wynajem przez Brokera: wniosek z oferty, "Moje wypożyczenia" ze statusem od wypożyczalni i zgłoszenie zwrotu.
/// Użytkownik widzi tylko swoje wynajmy (po profilu), cudze dają 404.
/// </summary>
public sealed class RentalService(
    IProviderRegistry registry,
    ProfileService profiles,
    BrokerDbContext db,
    TimeProvider clock,
    ILogger<RentalService> logger)
{
    public async Task<ServiceResult<RentalResponse>> CreateAsync(CreateRentalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var profileResult = await profiles.GetOrCreateCurrentAsync(cancellationToken);
        if (!profileResult.IsSuccess)
        {
            return profileResult.Error;
        }

        var profile = profileResult.Value;
        var quoteId = request.QuoteId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        // Oferta musi być tego użytkownika. Wycena anonimowa nie ma właściciela, więc trzeba ją powtórzyć po zalogowaniu.
        var quote = await db.Quotes
            .Include(q => q.Provider)
            .FirstOrDefaultAsync(q => q.Id == quoteId && q.UserProfileId == profile.Id, cancellationToken);

        if (quote is null)
        {
            return ServiceError.NotFound("Nie znaleziono oferty. Sprawdź cenę jeszcze raz po zalogowaniu.");
        }

        if (quote.IsExpired(now))
        {
            return ServiceError.Gone("Oferta wygasła. Sprawdź cenę jeszcze raz.");
        }

        if (await db.Rentals.AnyAsync(r => r.QuoteId == quote.Id, cancellationToken))
        {
            return ServiceError.Conflict("Ta oferta została już wykorzystana.");
        }

        var provider = await registry.FindAsync(quote.Provider!.Code, cancellationToken);
        if (provider is null)
        {
            return ServiceError.Unavailable("Wypożyczalnia jest teraz niedostępna.");
        }

        var firstName = request.FirstName!.Trim();
        var lastName = request.LastName!.Trim();
        var email = request.Email!.Trim();

        ProviderRental providerRental;
        try
        {
            providerRental = await provider.CreateRentalAsync(
                new RentalOrder(quote.ProviderOfferId, firstName, lastName, email), cancellationToken);
        }
        catch (ProviderRejectedException ex)
        {
            return ex.StatusCode switch
            {
                StatusCodes.Status400BadRequest => ServiceError.Validation(string.Empty, ex.Message),
                StatusCodes.Status404NotFound => ServiceError.Gone("Oferta nie jest już dostępna. Sprawdź cenę jeszcze raz."),
                StatusCodes.Status410Gone => ServiceError.Gone(ex.Message),
                _ => ServiceError.Conflict(ex.Message)
            };
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Wypożyczalnia {Provider} nie przyjęła wniosku o wynajem.", provider.Code);
            return ServiceError.Unavailable("Wypożyczalnia nie odpowiada. Spróbuj za chwilę.");
        }

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            UserProfileId = profile.Id,
            QuoteId = quote.Id,
            ProviderId = quote.ProviderId,
            Provider = quote.Provider,
            ProviderRentalId = providerRental.RentalId,
            ProviderCarId = quote.ProviderCarId,
            CarLabel = quote.CarLabel,
            StartDate = quote.StartDate,
            EndDate = quote.EndDate,
            TotalPrice = quote.TotalPrice,
            Currency = quote.Currency,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Status = providerRental.Status,
            Reason = providerRental.Reason,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Rentals.Add(rental);
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(rental);
    }

    /// <summary>Wynajmy użytkownika, najnowsze na górze. Statusy, które mogą się zmienić, odświeżamy u wypożyczalni.</summary>
    public async Task<ServiceResult<IReadOnlyList<RentalResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var profile = await profiles.FindCurrentAsync(cancellationToken);
        if (profile is null)
        {
            return Array.Empty<RentalResponse>();
        }

        var rentals = await db.Rentals
            .Include(r => r.Provider)
            .Where(r => r.UserProfileId == profile.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        await RefreshAsync(rentals, cancellationToken);
        return rentals.Select(ToResponse).ToList();
    }

    public async Task<ServiceResult<RentalResponse>> GetAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var rental = await FindOwnAsync(rentalId, cancellationToken);
        if (rental is null)
        {
            return ServiceError.NotFound("Nie znaleziono wypożyczenia.");
        }

        await RefreshAsync([rental], cancellationToken);
        return ToResponse(rental);
    }

    /// <summary>Zgłoszenie zwrotu. Broker wysyła email kierowcy z wniosku, bo wypożyczalnia po nim sprawdza właściciela.</summary>
    public async Task<ServiceResult<RentalResponse>> RequestReturnAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var rental = await FindOwnAsync(rentalId, cancellationToken);
        if (rental is null)
        {
            return ServiceError.NotFound("Nie znaleziono wypożyczenia.");
        }

        // Lokalnie może być jeszcze Pending, a wypożyczalnia już potwierdziła
        if (!rental.CanRequestReturn)
        {
            await RefreshAsync([rental], cancellationToken);
        }

        if (rental.Status == RentalStatus.ReturnRequested)
        {
            return ServiceError.Conflict("Zwrot został już zgłoszony.");
        }

        if (!rental.CanRequestReturn)
        {
            return ServiceError.Conflict("Zwrot można zgłosić tylko dla aktywnego wypożyczenia.");
        }

        var provider = await registry.FindAsync(rental.Provider!.Code, cancellationToken);
        if (provider is null)
        {
            return ServiceError.Unavailable("Wypożyczalnia jest teraz niedostępna.");
        }

        ProviderRental result;
        try
        {
            result = await provider.RequestReturnAsync(rental.ProviderRentalId, rental.Email, cancellationToken);
        }
        catch (ProviderRejectedException ex)
        {
            return ex.StatusCode == StatusCodes.Status404NotFound
                ? ServiceError.NotFound("Wypożyczalnia nie zna tego wypożyczenia.")
                : ServiceError.Conflict(ex.Message);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken))
        {
            logger.LogWarning(ex, "Wypożyczalnia {Provider} nie przyjęła zgłoszenia zwrotu.", provider.Code);
            return ServiceError.Unavailable("Wypożyczalnia nie odpowiada. Spróbuj za chwilę.");
        }

        rental.ApplyStatus(result.Status, null, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(rental);
    }

    private async Task<Rental?> FindOwnAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var profile = await profiles.FindCurrentAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        return await db.Rentals
            .Include(r => r.Provider)
            .FirstOrDefaultAsync(r => r.Id == rentalId && r.UserProfileId == profile.Id, cancellationToken);
    }

    /// <summary>
    /// Pyta wypożyczalnie o status wynajmów Pending i ReturnRequested (równolegle, tylko HTTP).
    /// Jak wypożyczalnia nie odpowiada, zostaje ostatni znany status.
    /// </summary>
    private async Task RefreshAsync(IReadOnlyList<Rental> rentals, CancellationToken cancellationToken)
    {
        var toCheck = rentals.Where(r => r.NeedsStatusCheck).ToList();
        if (toCheck.Count == 0)
        {
            return;
        }

        // Rejestr korzysta z bazy, więc pytamy go raz, przed równoległymi wywołaniami
        var active = await registry.GetActiveAsync(cancellationToken);
        var byCode = active.ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);

        var checks = toCheck
            .Where(r => byCode.ContainsKey(r.Provider!.Code))
            .Select(async r => (Rental: r, Status: await TryGetStatusAsync(byCode[r.Provider!.Code], r, cancellationToken)));
        var results = await Task.WhenAll(checks);

        var now = clock.GetUtcNow().UtcDateTime;
        var changed = false;
        foreach (var (rental, status) in results)
        {
            if (status is not null && rental.ApplyStatus(status.Status, status.Reason, now))
            {
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<ProviderRental?> TryGetStatusAsync(ICarRentalProvider provider, Rental rental, CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GetRentalAsync(rental.ProviderRentalId, cancellationToken);
        }
        catch (Exception ex) when (IsProviderFailure(ex, cancellationToken) || ex is ProviderRejectedException)
        {
            logger.LogWarning(ex, "Nie udało się sprawdzić statusu wynajmu {RentalId} u {Provider}.", rental.Id, provider.Code);
            return null;
        }
    }

    private static bool IsProviderFailure(Exception ex, CancellationToken cancellationToken) =>
        (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        && !cancellationToken.IsCancellationRequested;

    private static RentalResponse ToResponse(Rental rental) =>
        new(
            rental.Id,
            rental.Provider?.Code ?? string.Empty,
            rental.Provider?.Name ?? string.Empty,
            rental.ProviderRentalId,
            rental.ProviderCarId,
            rental.CarLabel,
            rental.StartDate,
            rental.EndDate,
            rental.TotalPrice,
            rental.Currency,
            rental.Status,
            rental.Reason,
            rental.CanRequestReturn,
            DateTime.SpecifyKind(rental.CreatedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(rental.UpdatedAtUtc, DateTimeKind.Utc));
}
