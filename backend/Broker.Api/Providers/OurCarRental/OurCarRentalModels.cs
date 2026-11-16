namespace Broker.Api.Providers.OurCarRental;

// Własne DTO odpowiedzi naszego Car Rental API. Celowo bez referencji do projektu CarRental.Api,
// bo dla Brokera to zewnętrzny system (tak samo jak API prowadzącego i innej grupy).

/// <summary>GET /api/v1/cars, GET /api/v1/cars/{id}.</summary>
internal sealed record OurCarDto(int Id, string Make, string Model, int Year, string? Location, bool Available);

/// <summary>Body POST /api/v1/offers.</summary>
internal sealed record OurCreateOfferDto(
    int CarId,
    int Age,
    int LicenseYears,
    string Location,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>Odpowiedź 201 z POST /api/v1/offers.</summary>
internal sealed record OurOfferDto(
    Guid OfferId,
    int CarId,
    decimal DailyPrice,
    decimal DailyInsurancePrice,
    decimal TotalPrice,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    DateTime ExpiresAt);
