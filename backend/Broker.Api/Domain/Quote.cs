namespace Broker.Api.Domain;

/// <summary>
/// Oferta otrzymana od wypożyczalni i pokazana użytkownikowi. Zapisujemy ją, żeby w KM3
/// zrobić z niej wynajem ("Rent me") i żeby wiedzieć, kiedy wygasa.
/// </summary>
public class Quote
{
    public Guid Id { get; set; }

    public int ProviderId { get; set; }
    public Provider? Provider { get; set; }

    /// <summary>Numer oferty u wypożyczalni.</summary>
    public string ProviderOfferId { get; set; } = string.Empty;

    /// <summary>Id auta u wypożyczalni.</summary>
    public string ProviderCarId { get; set; } = string.Empty;

    /// <summary>Opis auta do wyświetlenia, np. "Toyota Corolla 2022".</summary>
    public string CarLabel { get; set; } = string.Empty;

    public decimal DailyPrice { get; set; }
    public decimal DailyInsurancePrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "PLN";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Zalogowany użytkownik, który poprosił o wycenę. Null przy wycenie anonimowej.</summary>
    public Guid? UserProfileId { get; set; }
    public UserProfile? UserProfile { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;
}
