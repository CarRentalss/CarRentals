using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using CarRental.Api.Storage;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Services;

public sealed record AttachmentDownload(Stream Content, string ContentType, string FileName);

/// <summary>Operacje panelu pracownika naszej wypożyczalni (#100, #101, #126, #129).</summary>
public sealed class EmployeeRentalService(CarRentalDbContext db, IFileStorage storage, TimeProvider clock)
{
    public async Task<IReadOnlyList<EmployeeRentalListItem>> ListAsync(RentalStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Rentals.AsNoTracking();

        if (status is not null)
        {
            var wanted = status.Value;
            query = query.Where(r => r.Status == wanted);
        }

        var rows = await query
            .OrderBy(r => r.StartDate).ThenBy(r => r.CreatedAtUtc)
            .Select(r => new
            {
                r.Id,
                r.CarId,
                r.Car.Make,
                r.Car.Model,
                r.Car.Year,
                r.FirstName,
                r.LastName,
                r.Email,
                r.StartDate,
                r.EndDate,
                r.Status,
                r.TotalPrice
            })
            .ToListAsync(cancellationToken);

        // Tekst składamy już w pamięci, żeby nie zależeć od tego, jak baza łączy napisy z liczbami
        return rows
            .Select(r => new EmployeeRentalListItem(
                r.Id,
                r.CarId,
                $"{r.Make} {r.Model} ({r.Year})",
                $"{r.FirstName} {r.LastName}",
                r.Email,
                r.StartDate,
                r.EndDate,
                r.Status,
                r.TotalPrice))
            .ToList();
    }

    public async Task<EmployeeRentalDetails?> GetDetailsAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var rental = await db.Rentals.AsNoTracking()
            .Include(r => r.Car)
            .FirstOrDefaultAsync(r => r.Id == rentalId, cancellationToken);

        return rental is null ? null : ToDetails(rental);
    }

    public async Task<ServiceResult<RentalStatusChangedResponse>> ConfirmReturnAsync(
        Guid rentalId, string note, IFormFile file, string employeeName, CancellationToken cancellationToken)
    {
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == rentalId, cancellationToken);

        if (rental is null)
        {
            return ServiceError.NotFound("Nie znaleziono wynajmu.");
        }

        if (rental.Status != RentalStatus.ReturnRequested)
        {
            return ServiceError.Conflict($"Zwrot można potwierdzić tylko po zgłoszeniu zwrotu. Obecny status: {rental.Status}.");
        }

        var validation = await AttachmentValidator.ValidateAsync(file, cancellationToken);
        if (!validation.IsSuccess)
        {
            return validation.Error;
        }

        string key;
        await using (var content = file.OpenReadStream())
        {
            key = await storage.SaveAsync(content, validation.Value.Extension, cancellationToken);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        rental.Status = RentalStatus.Completed;
        rental.CompletedAtUtc = now;
        rental.UpdatedAtUtc = now;
        rental.CompletedBy = employeeName;
        rental.ReturnNote = note.Trim();
        rental.AttachmentKey = key;
        rental.AttachmentContentType = validation.Value.ContentType;
        rental.AttachmentFileName = Path.GetFileName(file.FileName);

        await db.SaveChangesAsync(cancellationToken);

        // Faktura PDF (QuestPDF) i mail do klienta dochodzą w KM3 (#82, #107, #108)
        return new RentalStatusChangedResponse(rental.Id, rental.Status);
    }

    public async Task<ServiceResult<AttachmentDownload>> GetAttachmentAsync(Guid rentalId, CancellationToken cancellationToken)
    {
        var rental = await db.Rentals.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rentalId, cancellationToken);

        if (rental?.AttachmentKey is null)
        {
            return ServiceError.NotFound("Brak załącznika dla tego wynajmu.");
        }

        var stream = await storage.OpenReadAsync(rental.AttachmentKey, cancellationToken);
        if (stream is null)
        {
            return ServiceError.NotFound("Plik załącznika nie istnieje.");
        }

        return new AttachmentDownload(
            stream,
            rental.AttachmentContentType ?? "application/octet-stream",
            rental.AttachmentFileName ?? "zalacznik");
    }

    private static EmployeeRentalDetails ToDetails(Rental r) => new(
        r.Id,
        r.CarId,
        $"{r.Car.Make} {r.Car.Model} ({r.Car.Year})",
        r.Car.Location,
        r.FirstName,
        r.LastName,
        r.Email,
        r.StartDate,
        r.EndDate,
        r.Status,
        r.Reason,
        r.TotalPrice,
        r.BrokerName,
        AsUtc(r.CreatedAtUtc),
        r.ReturnRequestedAtUtc is null ? null : AsUtc(r.ReturnRequestedAtUtc.Value),
        r.CompletedAtUtc is null ? null : AsUtc(r.CompletedAtUtc.Value),
        r.CompletedBy,
        r.ReturnNote,
        r.AttachmentKey is not null);

    // Baza oddaje DateTime bez informacji o strefie, a my zawsze zapisujemy UTC
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
