using System.Data;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using CarRental.Api.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarRental.Api.Services;

/// <summary>
/// Potwierdzanie wniosków o wynajem. Jedna klasa dla obu trybów:
/// na Azure woła ją CarRental.Worker po wiadomości z kolejki rental-requests,
/// lokalnie i w testach RentalConfirmationWorker w procesie API (co kilka sekund).
/// Plik jest linkowany do projektu CarRental.Worker, więc bez typów z ASP.NET Core.
/// </summary>
public sealed class RentalConfirmationProcessor(
    CarRentalDbContext db,
    IEmailSender emailSender,
    TimeProvider clock,
    ILogger<RentalConfirmationProcessor> logger)
{
    /// <summary>
    /// Sprawdza jeden wniosek: czy auto jest we flocie i czy termin jest wolny. Ustawia Confirmed albo Rejected
    /// i wysyła mail do klienta. Zwraca nowy status albo null, gdy nie było czego robić
    /// (nie ma takiego wynajmu albo już nie czeka, np. ta sama wiadomość przyszła drugi raz).
    /// </summary>
    public async Task<RentalStatus?> ProcessAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        // Strategia wykonania, bo na Azure SQL jest EnableRetryOnFailure (tak samo jak w RentalService.CreateAsync)
        var strategy = db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(
            token => ProcessInTransactionAsync(rentalId, token),
            cancellationToken);

        if (result is null)
        {
            return null;
        }

        await NotifyAsync(result, cancellationToken);
        return result.Status;
    }

    /// <summary>Tryb bez kolejki: przetwarza wszystkie wnioski, które czekają dłużej niż <paramref name="olderThan"/>.</summary>
    public async Task<int> ProcessPendingAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        var cutoff = clock.GetUtcNow().UtcDateTime - olderThan;

        var ids = await db.Rentals.AsNoTracking()
            .Where(r => r.Status == RentalStatus.Pending && r.CreatedAtUtc <= cutoff)
            .OrderBy(r => r.CreatedAtUtc)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await ProcessAsync(id, cancellationToken) is not null)
                {
                    processed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Jeden zły wniosek nie blokuje pozostałych, spróbujemy przy następnej rundzie
                logger.LogError(ex, "Nie udało się przetworzyć wniosku {RentalId}.", id);
            }
        }

        return processed;
    }

    private async Task<RentalEmailData?> ProcessInTransactionAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        // Serializable: sprawdzenie terminu i zmiana statusu dzieją się razem, nikt nie wejdzie w środek
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var rental = await db.Rentals
            .Include(r => r.Car)
            .FirstOrDefaultAsync(r => r.Id == rentalId, cancellationToken);

        if (rental is null)
        {
            logger.LogWarning("Wniosek {RentalId} nie istnieje, pomijam.", rentalId);
            return null;
        }

        if (rental.Status != RentalStatus.Pending)
        {
            logger.LogInformation("Wniosek {RentalId} ma już status {Status}, pomijam.", rentalId, rental.Status);
            return null;
        }

        var carIsFree = !await db.Rentals
            .BlockingCar(rental.CarId)
            .Overlapping(rental.StartDate, rental.EndDate)
            .AnyAsync(r => r.Id != rental.Id, cancellationToken);

        rental.Process(rental.Car.IsActive, carIsFree, clock.GetUtcNow().UtcDateTime);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Wniosek {RentalId}: {Status}.", rental.Id, rental.Status);
        return RentalEmailData.From(rental);
    }

    private async Task NotifyAsync(RentalEmailData data, CancellationToken cancellationToken)
    {
        var message = data.Status == RentalStatus.Confirmed
            ? EmailTemplates.RentalConfirmed(data)
            : EmailTemplates.RentalRejected(data);

        try
        {
            await emailSender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Status jest już zapisany. Brak maila nie cofa decyzji, klient widzi wynik w aplikacji.
            logger.LogError(ex, "Nie udało się wysłać maila dla wniosku {RentalId}.", data.RentalId);
        }
    }
}
