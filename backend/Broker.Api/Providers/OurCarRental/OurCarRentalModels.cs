using Broker.Api.Contracts;

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

/// <summary>Body POST /api/v1/rentals.</summary>
internal sealed record OurCreateRentalDto(Guid OfferId, string FirstName, string LastName, string Email);

/// <summary>Odpowiedź 202 z POST /api/v1/rentals i 200 z POST /api/v1/rentals/{id}/return.</summary>
internal sealed record OurRentalChangedDto(Guid RentalId, RentalStatus Status);

/// <summary>GET /api/v1/rentals/{id}.</summary>
internal sealed record OurRentalStatusDto(Guid RentalId, RentalStatus Status, string? Reason);

/// <summary>Body POST /api/v1/rentals/{id}/return.</summary>
internal sealed record OurReturnDto(string Email);
