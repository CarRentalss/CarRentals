using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Contracts;

/// <summary>Wniosek o wynajem z oferty (POST /api/v1/rentals). Dane kierowcy front wypełnia z profilu.</summary>
public sealed class CreateRentalRequest
{
    /// <summary>quoteId z odpowiedzi POST /api/v1/offers.</summary>
    [Required]
    public Guid? QuoteId { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? FirstName { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? LastName { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}

/// <summary>Wynajem zalogowanego użytkownika ("Moje wypożyczenia").</summary>
public sealed record RentalResponse(
    Guid RentalId,
    string Provider,
    string ProviderName,
    string ProviderRentalId,
    string CarId,
    string CarLabel,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal TotalPrice,
    string Currency,
    RentalStatus Status,
    string? Reason,
    bool CanRequestReturn,
    DateTime CreatedAt,
    DateTime UpdatedAt);
