using CarRental.Api.Domain;
using CarRental.Api.Options;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Pricing;

public sealed record PriceQuote(decimal DailyPrice, decimal DailyInsurancePrice, int Days, decimal TotalPrice);

/// <summary>
/// Liczy cenę oferty: cena bazowa auta razy mnożniki ryzyka.
/// Kierowca poniżej 25 lat i kierowca z prawem jazdy krócej niż 3 lata płacą więcej.
/// Mnożniki dotyczą też ubezpieczenia, bo ryzyko jest to samo.
/// </summary>
public sealed class PriceCalculator(IOptions<PricingOptions> options)
{
    private readonly PricingOptions _options = options.Value;

    public decimal GetRiskMultiplier(int driverAge, int licenseYears)
    {
        var multiplier = 1m;

        if (driverAge < _options.YoungDriverAgeLimit)
        {
            multiplier *= _options.YoungDriverMultiplier;
        }

        if (licenseYears < _options.NewDriverYearsLimit)
        {
            multiplier *= _options.NewDriverMultiplier;
        }

        return multiplier;
    }

    public PriceQuote Calculate(Car car, int driverAge, int licenseYears, DateOnly startDate, DateOnly endDate)
    {
        ArgumentNullException.ThrowIfNull(car);

        // Dzień końcowy to dzień oddania auta, więc 10.11 - 13.11 to 3 doby
        var days = endDate.DayNumber - startDate.DayNumber;
        if (days <= 0)
        {
            throw new ArgumentException("Data zakończenia musi być późniejsza niż data rozpoczęcia.", nameof(endDate));
        }

        var multiplier = GetRiskMultiplier(driverAge, licenseYears);
        var dailyPrice = RoundMoney(car.BasePricePerDay * multiplier);
        var dailyInsurance = RoundMoney(car.InsurancePricePerDay * multiplier);
        var total = (dailyPrice + dailyInsurance) * days;

        return new PriceQuote(dailyPrice, dailyInsurance, days, total);
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
