using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using CarRental.Api.Images;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Services;

/// <summary>Katalog aut dla brokerów (#117, #118).</summary>
public sealed class CarCatalogService(CarRentalDbContext db, TimeProvider clock, CarImages images)
{
    public async Task<IReadOnlyList<CarResponse>> GetCarsAsync(string? make, string? model, int? year, CancellationToken cancellationToken)
    {
        var query = db.Cars.AsNoTracking().Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(make))
        {
            var normalizedMake = make.Trim().ToLower();
            query = query.Where(c => c.Make.ToLower() == normalizedMake);
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            var normalizedModel = model.Trim().ToLower();
            query = query.Where(c => c.Model.ToLower() == normalizedModel);
        }

        if (year is not null)
        {
            query = query.Where(c => c.Year == year);
        }

        var today = Today();

        // Uwaga: warunek "zajęte dziś" jest wpisany w projekcję wprost,
        // bo EF Core nie przetłumaczy na SQL wywołania naszej metody wewnątrz Select.
        var rows = await query
            .OrderBy(c => c.Make).ThenBy(c => c.Model).ThenBy(c => c.Id)
            .Select(c => new CarRow(
                c.Id, c.Make, c.Model, c.Year, c.Location, c.ImageUrl,
                !db.Rentals.Any(r =>
                    r.CarId == c.Id &&
                    (r.Status == RentalStatus.Pending ||
                     r.Status == RentalStatus.Confirmed ||
                     r.Status == RentalStatus.ReturnRequested) &&
                    r.StartDate <= today && today < r.EndDate)))
            .ToListAsync(cancellationToken);

        return rows.Select(ToResponse).ToList();
    }

    public async Task<CarResponse?> GetCarAsync(int id, CancellationToken cancellationToken)
    {
        var today = Today();

        var row = await db.Cars.AsNoTracking()
            .Where(c => c.Id == id && c.IsActive)
            .Select(c => new CarRow(
                c.Id, c.Make, c.Model, c.Year, c.Location, c.ImageUrl,
                !db.Rentals.Any(r =>
                    r.CarId == c.Id &&
                    (r.Status == RentalStatus.Pending ||
                     r.Status == RentalStatus.Confirmed ||
                     r.Status == RentalStatus.ReturnRequested) &&
                    r.StartDate <= today && today < r.EndDate)))
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToResponse(row);
    }

    // Adres zdjęcia składamy już w pamięci, bo zależy od konfiguracji (Images:BaseUrl), a nie od bazy
    private CarResponse ToResponse(CarRow c) =>
        new(c.Id, c.Make, c.Model, c.Year, c.Location, c.Available, images.UrlFor(c.ImageUrl), images.CreditFor(c.ImageUrl));

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private sealed record CarRow(int Id, string Make, string Model, int Year, string Location, string? ImageUrl, bool Available);
}
