using Broker.Api.Contracts;

namespace Broker.Api.Domain;

/// <summary>
/// Wynajem złożony przez Brokera u wypożyczalni. Dane z oferty (auto, termin, cena) kopiujemy tutaj,
/// bo stare oferty usuwa QuoteCleanupWorker, a wynajem musi zostać. Status odświeżamy u wypożyczalni.
/// </summary>
public class Rental
{
    public Guid Id { get; set; }

    /// <summary>Właściciel wynajmu (profil zalogowanego użytkownika).</summary>
    public Guid UserProfileId { get; set; }
    public UserProfile? UserProfile { get; set; }

    /// <summary>Oferta, z której powstał wynajem. Null, gdy oferta została już usunięta przy sprzątaniu.</summary>
    public Guid? QuoteId { get; set; }
    public Quote? Quote { get; set; }

    public int ProviderId { get; set; }
    public Provider? Provider { get; set; }

    /// <summary>Numer wynajmu u wypożyczalni.</summary>
    public string ProviderRentalId { get; set; } = string.Empty;

    public string ProviderCarId { get; set; } = string.Empty;
    public string CarLabel { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "PLN";

    // Dane kierowcy wysłane do wypożyczalni (email jest potrzebny przy zgłoszeniu zwrotu)
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public RentalStatus Status { get; set; } = RentalStatus.Pending;

    /// <summary>Powód odrzucenia od wypożyczalni.</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Status może się zmienić po stronie wypożyczalni (potwierdzenie, odbiór auta przez pracownika).</summary>
    public bool NeedsStatusCheck => Status is RentalStatus.Pending or RentalStatus.ReturnRequested;

    /// <summary>Zwrot zgłaszamy tylko dla aktywnego (potwierdzonego) wynajmu.</summary>
    public bool CanRequestReturn => Status == RentalStatus.Confirmed;

    /// <summary>Zapisuje status od wypożyczalni. Zwraca true, jak coś się zmieniło.</summary>
    public bool ApplyStatus(RentalStatus status, string? reason, DateTime nowUtc)
    {
        if (Status == status && Reason == reason)
        {
            return false;
        }

        Status = status;
        Reason = reason;
        UpdatedAtUtc = nowUtc;
        return true;
    }
}
