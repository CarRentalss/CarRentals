using System.ComponentModel.DataAnnotations;
using CarRental.Api.Domain;
using Microsoft.AspNetCore.Http;

namespace CarRental.Api.Contracts;

// Modele zapytań i odpowiedzi zgodne ze szkieletem kontraktu (#26).
// Pola z wymagań są zawsze, kilka dodatkowych (np. totalPrice) ułatwia życie brokerowi.

/// <summary>Auto z katalogu (GET /cars).</summary>
public sealed record CarResponse(int Id, string Make, string Model, int Year, string Location, bool Available);

/// <summary>Zapytanie o ofertę (POST /offers).</summary>
public sealed class CreateOfferRequest
{
    [Required]
    public int? CarId { get; init; }

    /// <summary>Wiek kierowcy.</summary>
    [Required, Range(18, 99)]
    public int? Age { get; init; }

    /// <summary>Ile lat kierowca ma prawo jazdy.</summary>
    [Required, Range(0, 81)]
    public int? LicenseYears { get; init; }

    /// <summary>Miasto odbioru auta.</summary>
    [Required, StringLength(100, MinimumLength = 2)]
    public string? Location { get; init; }

    [Required]
    public DateOnly? StartDate { get; init; }

    [Required]
    public DateOnly? EndDate { get; init; }
}

/// <summary>Oferta (odpowiedź POST /offers).</summary>
public sealed record OfferResponse(
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

/// <summary>Wniosek o wynajem (POST /rentals).</summary>
public sealed class CreateRentalRequest
{
    [Required]
    public Guid? OfferId { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? FirstName { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? LastName { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}

/// <summary>Odpowiedź po złożeniu wniosku albo zmianie statusu.</summary>
public sealed record RentalStatusChangedResponse(Guid RentalId, RentalStatus Status);

/// <summary>Status wynajmu (GET /rentals/{rentalId}).</summary>
public sealed record RentalStatusResponse(
    Guid RentalId,
    RentalStatus Status,
    string? Reason,
    int CarId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice);

/// <summary>Zgłoszenie zwrotu (POST /rentals/{rentalId}/return).</summary>
public sealed class ReturnRequest
{
    /// <summary>Email klienta, sprawdzamy czy to jego wynajem.</summary>
    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}

/// <summary>Wiersz na liście wynajmów w panelu pracownika.</summary>
public sealed record EmployeeRentalListItem(
    Guid RentalId,
    int CarId,
    string Car,
    string CustomerName,
    string Email,
    DateOnly StartDate,
    DateOnly EndDate,
    RentalStatus Status,
    decimal TotalPrice);

/// <summary>Szczegóły wynajmu w panelu pracownika.</summary>
public sealed record EmployeeRentalDetails(
    Guid RentalId,
    int CarId,
    string Car,
    string Location,
    string FirstName,
    string LastName,
    string Email,
    DateOnly StartDate,
    DateOnly EndDate,
    RentalStatus Status,
    string? Reason,
    decimal TotalPrice,
    string BrokerName,
    DateTime CreatedAt,
    DateTime? ReturnRequestedAt,
    DateTime? CompletedAt,
    string? CompletedBy,
    string? ReturnNote,
    bool HasAttachment);

/// <summary>Formularz potwierdzenia zwrotu (multipart/form-data).</summary>
public sealed class ConfirmReturnForm
{
    /// <summary>Krótka notatka o stanie auta.</summary>
    [Required, StringLength(1000, MinimumLength = 3)]
    public string? Note { get; init; }

    /// <summary>Zdjęcie (jpg, png) albo PDF, do 10 MB.</summary>
    [Required]
    public IFormFile? File { get; init; }
}
