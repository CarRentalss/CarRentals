namespace Broker.Api.Providers;

/// <summary>
/// Wspólny interfejs wypożyczalni (wzorzec Adapter). Każde API (nasze, prowadzącego, innej grupy)
/// ma własną klasę, która tłumaczy jego odpowiedzi na wspólny model. Nowy dostawca = nowa klasa.
/// </summary>
public interface ICarRentalProvider
{
    /// <summary>Kod z tabeli Providers, np. "our".</summary>
    string Code { get; }

    string DisplayName { get; }

    /// <summary>Czy w konfiguracji jest adres i klucz.</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<ProviderCar>> GetCarsAsync(CancellationToken cancellationToken);

    /// <summary>Jedno auto albo null, jak go nie ma.</summary>
    Task<ProviderCar?> GetCarAsync(string carId, CancellationToken cancellationToken);

    /// <summary>Oferta. Gdy wypożyczalnia odmówi (zajęte auto, zła lokalizacja), rzuca <see cref="ProviderRejectedException"/>.</summary>
    Task<ProviderQuote> CreateOfferAsync(OfferQuery query, CancellationToken cancellationToken);
}

/// <summary>Auto we wspólnym modelu Brokera.</summary>
public sealed record ProviderCar(string CarId, string Make, string Model, int Year, string? Location, bool Available)
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
