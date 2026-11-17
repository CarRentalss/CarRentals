using CarRental.Api.Domain;

namespace CarRental.Api.IntegrationTests;

/// <summary>Dane testowe: oferta i wynajem z sensownymi wartościami domyślnymi.</summary>
public static class TestData
{
    public static Offer Offer(int carId = 1, DateTime? nowUtc = null, DateOnly? start = null, DateOnly? end = null, string broker = "OurBroker")
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new Offer
        {
            Id = Guid.NewGuid(),
            CarId = carId,
            DriverAge = 30,
            LicenseYears = 10,
            Location = "Warszawa",
            StartDate = start ?? new DateOnly(2030, 1, 10),
            EndDate = end ?? new DateOnly(2030, 1, 13),
            DailyPrice = 150m,
            DailyInsurancePrice = 30m,
            TotalPrice = 540m,
            Currency = "PLN",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            BrokerName = broker
        };
    }

    public static Rental RentalFor(Offer offer, DateTime? nowUtc = null) =>
        offer.CreateRental("Jan", "Kowalski", "jan.kowalski@example.com", nowUtc ?? DateTime.UtcNow);
}
