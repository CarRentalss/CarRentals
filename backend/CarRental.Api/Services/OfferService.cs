using CarRental.Api.Contracts;
using CarRental.Api.Data;
using CarRental.Api.Domain;
using CarRental.Api.Options;
using CarRental.Api.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Services;

/// <summary>Tworzenie ofert z wyceną (#119).</summary>
public sealed class OfferService(
    CarRentalDbContext db,
    PriceCalculator priceCalculator,
    IOptions<OfferOptions> offerOptions,
    IOptions<PricingOptions> pricingOptions,
    TimeProvider clock)
{
    public async Task<ServiceResult<OfferResponse>> CreateAsync(CreateOfferRequest request, string brokerName, CancellationToken cancellationToken)
    {
        // [Required] w modelu gwarantuje, że wartości są podane
        var carId = request.CarId!.Value;
        var age = request.Age!.Value;
        var licenseYears = request.LicenseYears!.Value;
        var location = request.Location!.Trim();
        var startDate = request.StartDate!.Value;
        var endDate = request.EndDate!.Value;

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var pricing = pricingOptions.Value;

        if (startDate < today)
        {
            return ServiceError.Validation("startDate", "Data rozpoczęcia nie może być w przeszłości.");
        }

        if (endDate <= startDate)
        {
            return ServiceError.Validation("endDate", "Data zakończenia musi być późniejsza niż data rozpoczęcia.");
        }

        if (endDate.DayNumber - startDate.DayNumber > pricing.MaxRentalDays)
        {
            return ServiceError.Validation("endDate", $"Wynajem może trwać najwyżej {pricing.MaxRentalDays} dni.");
        }

        if (licenseYears > age - 18)
        {
            return ServiceError.Validation("licenseYears", "Staż prawa jazdy nie może być dłuższy niż wiek minus 18 lat.");
        }

        var car = await db.Cars.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == carId && c.IsActive, cancellationToken);

        if (car is null)
        {
            return ServiceError.NotFound("Nie znaleziono samochodu.");
        }

        if (!string.Equals(car.Location, location, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceError.Validation("location", $"Ten samochód można odebrać tylko w lokalizacji: {car.Location}.");
        }

        // Katalog może być w cache brokera do 30 min, więc dostępność sprawdzamy tutaj i drugi raz przy wynajmie
        var isTaken = await db.Rentals
            .BlockingCar(carId)
            .Overlapping(startDate, endDate)
            .AnyAsync(cancellationToken);

        if (isTaken)
        {
            return ServiceError.Conflict("Samochód jest zajęty w wybranym terminie.");
        }

        var quote = priceCalculator.Calculate(car, age, licenseYears, startDate, endDate);

        var offer = new Offer
        {
            Id = Guid.NewGuid(),
            CarId = car.Id,
            DriverAge = age,
            LicenseYears = licenseYears,
            Location = car.Location,
            StartDate = startDate,
            EndDate = endDate,
            DailyPrice = quote.DailyPrice,
            DailyInsurancePrice = quote.DailyInsurancePrice,
            TotalPrice = quote.TotalPrice,
            Currency = pricing.Currency,
            CreatedAtUtc = now.UtcDateTime,
            ExpiresAtUtc = now.AddMinutes(offerOptions.Value.ValidityMinutes).UtcDateTime,
            BrokerName = brokerName
        };

        db.Offers.Add(offer);
        await db.SaveChangesAsync(cancellationToken);

        return new OfferResponse(
            offer.Id,
            offer.CarId,
            offer.DailyPrice,
            offer.DailyInsurancePrice,
            offer.TotalPrice,
            offer.Currency,
            offer.StartDate,
            offer.EndDate,
            quote.Days,
            DateTime.SpecifyKind(offer.ExpiresAtUtc, DateTimeKind.Utc));
    }
}
