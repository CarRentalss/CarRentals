using System.Data;
using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Services;

/// <summary>Wynajmy po stronie brokera: wniosek, status, zgłoszenie zwrotu (#120, #121, #125, #131, #132).</summary>
public sealed class RentalService(CarRentalDbContext db, TimeProvider clock, ILogger<RentalService> logger)
{
    public async Task<ServiceResult<RentalStatusChangedResponse>> CreateAsync(
        CreateRentalRequest request, string brokerName, CancellationToken cancellationToken)
    {
        // Strategia wykonania jest potrzebna, bo na Azure SQL włączamy ponawianie (EnableRetryOnFailure),
        // a wtedy własną transakcję trzeba w nią opakować.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            token => CreateInTransactionAsync(request, brokerName, token),
            cancellationToken);
    }

    private async Task<ServiceResult<RentalStatusChangedResponse>> CreateInTransactionAsync(
        CreateRentalRequest request, string brokerName, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow().UtcDateTime;
        var offerId = request.OfferId!.Value;

        // Serializable: dwa wnioski na to samo auto w tym samym czasie nie przejdą oba
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        // Broker widzi tylko swoje oferty
        var offer = await db.Offers
            .FirstOrDefaultAsync(o => o.Id == offerId && o.BrokerName == brokerName, cancellationToken);

        if (offer is null)
        {
            return ServiceError.NotFound("Nie znaleziono oferty.");
        }

        if (now > offer.ExpiresAtUtc)
        {
            return ServiceError.Gone("Oferta wygasła. Poproś o nową ofertę.");
        }

        if (await db.Rentals.AnyAsync(r => r.OfferId == offer.Id, cancellationToken))
        {
            return ServiceError.Conflict("Ta oferta została już wykorzystana.");
        }

        var isTaken = await db.Rentals
            .BlockingCar(offer.CarId)
            .Overlapping(offer.StartDate, offer.EndDate)
            .AnyAsync(cancellationToken);

        if (isTaken)
        {
            return ServiceError.Conflict("Samochód został już wynajęty w tym terminie.");
        }

        var rental = new Rental
        {
            Id = Guid.NewGuid(),
            OfferId = offer.Id,
            CarId = offer.CarId,
            StartDate = offer.StartDate,
            EndDate = offer.EndDate,
            TotalPrice = offer.TotalPrice,
            FirstName = request.FirstName!.Trim(),
            LastName = request.LastName!.Trim(),
            Email = request.Email!.Trim(),
            Status = RentalStatus.Pending,
            BrokerName = brokerName,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.Rentals.Add(rental);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Np. unikalny indeks na OfferId, gdy dwa zapytania przyszły w tej samej chwili
            logger.LogWarning(ex, "Nie udało się zapisać wynajmu dla oferty {OfferId}", offer.Id);
            db.ChangeTracker.Clear();
            return ServiceError.Conflict("Nie udało się zarezerwować auta, spróbuj ponownie z nową ofertą.");
        }

        return new RentalStatusChangedResponse(rental.Id, rental.Status);
    }

    public async Task<ServiceResult<RentalStatusResponse>> GetStatusAsync(
        Guid rentalId, string brokerName, CancellationToken cancellationToken)
    {
        var rental = await db.Rentals.AsNoTracking()
            .Where(r => r.Id == rentalId && r.BrokerName == brokerName)
            .Select(r => new RentalStatusResponse(r.Id, r.Status, r.Reason, r.CarId, r.StartDate, r.EndDate, r.TotalPrice))
            .FirstOrDefaultAsync(cancellationToken);

        return rental is null
            ? ServiceError.NotFound("Nie znaleziono wynajmu.")
            : rental;
    }

    public async Task<ServiceResult<RentalStatusChangedResponse>> RequestReturnAsync(
        Guid rentalId, string email, string brokerName, CancellationToken cancellationToken)
    {
        var rental = await db.Rentals
            .FirstOrDefaultAsync(r => r.Id == rentalId && r.BrokerName == brokerName, cancellationToken);

        if (rental is null)
        {
            return ServiceError.NotFound("Nie znaleziono wynajmu.");
        }

        if (!string.Equals(rental.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return ServiceError.Forbidden("Adres e-mail nie pasuje do tego wynajmu.");
        }

        if (rental.Status == RentalStatus.ReturnRequested)
        {
            return ServiceError.Conflict("Zwrot został już zgłoszony.");
        }

        if (rental.Status != RentalStatus.Confirmed)
        {
            return ServiceError.Conflict($"Nie można zgłosić zwrotu dla wynajmu w statusie {rental.Status}.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        rental.Status = RentalStatus.ReturnRequested;
        rental.ReturnRequestedAtUtc = now;
        rental.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        return new RentalStatusChangedResponse(rental.Id, rental.Status);
    }

    /// <summary>
    /// Potwierdza wnioski, które czekają dłużej niż <paramref name="delay"/>.
    /// Wywołuje to proces w tle (RentalConfirmationWorker). Auto wycofane z floty powoduje odrzucenie.
    /// </summary>
    public async Task<int> ConfirmPendingAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var cutoff = now - delay;

        var pending = await db.Rentals
            .Include(r => r.Car)
            .Where(r => r.Status == RentalStatus.Pending && r.CreatedAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        foreach (var rental in pending)
        {
            if (rental.Car.IsActive)
            {
                rental.Status = RentalStatus.Confirmed;
            }
            else
            {
                rental.Status = RentalStatus.Rejected;
                rental.Reason = "Samochód został wycofany z floty.";
            }

            rental.UpdatedAtUtc = now;
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }
}
