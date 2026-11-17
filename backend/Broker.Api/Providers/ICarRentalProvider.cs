using Broker.Api.Contracts;

namespace Broker.Api.Providers;

/// <summary>
/// Wspólny interfejs wypożyczalni (wzorzec Adapter). Klasa tłumaczy odpowiedzi API na wspólny model.
/// Nasze API ma własną klasę (OurCarRentalClient). Typowe API podpinamy samą konfiguracją
/// (Generic/ConfigurableHttpProvider), a własną klasę piszemy tylko, jak mapowanie nie wystarczy.
/// </summary>
public interface ICarRentalProvider
{
    /// <summary>Kod z tabeli Providers, np. "our".</summary>
    string Code { get; }

    string DisplayName { get; }

    /// <summary>Providers:&lt;Klucz&gt;:Enabled. Jedyne źródło prawdy o tym, czy wypożyczalnia jest włączona.</summary>
    bool IsEnabled { get; }

    /// <summary>Czy w konfiguracji jest adres i klucz.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<ProviderCar>> GetCarsAsync(CancellationToken cancellationToken);

    /// <summary>Jedno auto albo null, jak go nie ma.</summary>
    Task<ProviderCar?> GetCarAsync(string carId, CancellationToken cancellationToken);

    /// <summary>Oferta. Gdy wypożyczalnia odmówi (zajęte auto, zła lokalizacja), rzuca <see cref="ProviderRejectedException"/>.</summary>
    Task<ProviderQuote> CreateOfferAsync(OfferQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Wniosek o wynajem na podstawie oferty. Wypożyczalnia zwykle odpowiada statusem Pending, wynik sprawdzamy później.
    /// Odmowa (oferta wygasła albo wykorzystana, auto zajęte) to <see cref="ProviderRejectedException"/> z kodem HTTP.
    /// </summary>
    Task<ProviderRental> CreateRentalAsync(RentalOrder order, CancellationToken cancellationToken);

    /// <summary>Aktualny status wynajmu albo null, jak wypożyczalnia go nie zna.</summary>
    Task<ProviderRental?> GetRentalAsync(string rentalId, CancellationToken cancellationToken);

    /// <summary>Klient zgłasza zwrot. Odmowa (zły status, inny email) to <see cref="ProviderRejectedException"/>.</summary>
    Task<ProviderRental> RequestReturnAsync(string rentalId, string email, CancellationToken cancellationToken);
}

/// <summary>Auto we wspólnym modelu Brokera.</summary>
/// <remarks>ImageUrl: pełny adres https zdjęcia, ImageCredit: autor i licencja zdjęcia (oba opcjonalne).</remarks>
public sealed record ProviderCar(
    string CarId,
    string Make,
    string Model,
    int Year,
    string? Location,
    bool Available,
    string? ImageUrl = null,
    string? ImageCredit = null)
{
    public string Label => $"{Make} {Model} {Year}";
}

/// <summary>Dane do wyceny wysyłane do wypożyczalni.</summary>
public sealed record OfferQuery(
    string CarId,
    int Age,
    int LicenseYears,
    string Location,
    DateOnly StartDate,
    DateOnly EndDate);

/// <summary>Oferta wypożyczalni we wspólnym modelu Brokera.</summary>
public sealed record ProviderQuote(
    string OfferId,
    string CarId,
    decimal DailyPrice,
    decimal DailyInsurancePrice,
    decimal TotalPrice,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    DateTime ExpiresAtUtc);

/// <summary>Dane do wniosku o wynajem: numer oferty u wypożyczalni i dane kierowcy.</summary>
public sealed record RentalOrder(string OfferId, string FirstName, string LastName, string Email);

/// <summary>Wynajem u wypożyczalni: jej numer, status i powód odrzucenia (jak jest).</summary>
public sealed record ProviderRental(string RentalId, RentalStatus Status, string? Reason = null);

/// <summary>Wypożyczalnia odpowiedziała, ale odmówiła (np. 400, 404, 409). Komunikat pokazujemy użytkownikowi.</summary>
public sealed class ProviderRejectedException : Exception
{
    public ProviderRejectedException()
    {
    }

    public ProviderRejectedException(string message) : base(message)
    {
    }

    public ProviderRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ProviderRejectedException(string message, int statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
