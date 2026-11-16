using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Contracts;

/// <summary>
/// Prośba o wycenę (POST /api/v1/offers). Anonimowo wiek, staż i lokalizacja są wymagane,
/// zalogowanemu brakujące dane bierzemy z profilu.
/// </summary>
public sealed class QuoteRequest
{
    /// <summary>Kod wypożyczalni auta, np. our.</summary>
    [Required, StringLength(50)]
    public string? Provider { get; init; }

    /// <summary>Id auta u tej wypożyczalni.</summary>
    [Required, StringLength(100)]
    public string? CarId { get; init; }

    [Required]
    public DateOnly? StartDate { get; init; }

    [Required]
    public DateOnly? EndDate { get; init; }

    /// <summary>Wiek kierowcy.</summary>
    [Range(18, 99)]
    public int? Age { get; init; }

    /// <summary>Ile lat kierowca ma prawo jazdy.</summary>
    [Range(0, 81)]
    public int? LicenseYears { get; init; }

    /// <summary>Miasto odbioru auta.</summary>
    [StringLength(100, MinimumLength = 2)]
    public string? Location { get; init; }
}

/// <summary>Oferta jednej wypożyczalni.</summary>
public sealed record ProviderOffer(
    Guid QuoteId,
    string Provider,
    string ProviderName,
    string OfferId,
    string CarId,
    string CarLabel,
    decimal DailyPrice,
    decimal DailyInsurancePrice,
    decimal TotalPrice,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    DateTime ExpiresAt);

/// <summary>Wynik wyceny: oferty i wypożyczalnie pominięte (limit 15 s albo błąd).</summary>
public sealed record QuoteResult(IReadOnlyList<ProviderOffer> Offers, IReadOnlyList<SkippedProvider> SkippedProviders);
