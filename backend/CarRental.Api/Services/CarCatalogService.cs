using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace CarRental.Api.Services;

/// <summary>Katalog aut dla brokerów (#117, #118).</summary>
public sealed class CarCatalogService(CarRentalDbContext db, TimeProvider clock)
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
        return await query
            .OrderBy(c => c.Make).ThenBy(c => c.Model).ThenBy(c => c.Id)
            .Select(c => new CarResponse(
                c.Id, c.Make, c.Model, c.Year, c.Location,
                !db.Rentals.Any(r =>
                    r.CarId == c.Id &&
                    (r.Status == RentalStatus.Pending ||
                     r.Status == RentalStatus.Confirmed ||
                     r.Status == RentalStatus.ReturnRequested) &&
                    r.StartDate <= today && today < r.EndDate)))
            .ToListAsync(cancellationToken);
    }

    public async Task<CarResponse?> GetCarAsync(int id, CancellationToken cancellationToken)
    {
        var today = Today();

        return await db.Cars.AsNoTracking()
            .Where(c => c.Id == id && c.IsActive)
            .Select(c => new CarResponse(
                c.Id, c.Make, c.Model, c.Year, c.Location,
                !db.Rentals.Any(r =>
                    r.CarId == c.Id &&
                    (r.Status == RentalStatus.Pending ||
                     r.Status == RentalStatus.Confirmed ||
                     r.Status == RentalStatus.ReturnRequested) &&
                    r.StartDate <= today && today < r.EndDate)))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
