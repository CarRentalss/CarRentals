namespace Broker.Api.Providers.Generic;

/// <summary>
/// Wypożyczalnia podłączona samą konfiguracją (sekcja Providers:&lt;Klucz&gt; z Type = Generic).
/// Domyślne ścieżki i nazwy pól są takie jak w naszym Car Rental API, więc dla API o tym samym kształcie
/// wystarczy BaseUrl, ApiKey i Enabled = true. Inny kształt opisujemy w Operations i Mapping.
/// Opis krok po kroku: docs/podpiecie-zewnetrznego-api.md.
/// </summary>
public sealed class GenericProviderOptions : ProviderEndpointOptions
{
    public const string GenericType = "Generic";

    /// <summary>
    /// Kod wypożyczalni (pole provider w API Brokera, kolumna Code w tabeli Providers).
    /// Puste = z nazwy sekcji: Lecturer daje lecturer, OtherGroup daje other-group.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Musi być Generic, inaczej sekcja jest pomijana.</summary>
    public string? Type { get; set; }

    public ProviderAuthOptions Auth { get; set; } = new();

    public ProviderOperationsOptions Operations { get; set; } = new();

    public ProviderMappingOptions Mapping { get; set; } = new();

    /// <summary>Adres jest poprawny i jest to, czego wymaga wybrany sposób uwierzytelnienia.</summary>
    public override bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && Auth.IsConfigured(ApiKey);
}

public enum ProviderAuthType
{
    /// <summary>Klucz w nagłówku (domyślnie x-api-key, nazwa w Auth:HeaderName).</summary>
    ApiKey,

    /// <summary>Authorization: Bearer &lt;ApiKey&gt; (token statyczny).</summary>
    Bearer,

    /// <summary>OAuth2 client credentials: token z Auth:TokenUrl, trzymany w pamięci do wygaśnięcia.</summary>
    OAuth2,

    /// <summary>Bez uwierzytelnienia.</summary>
    None
}

public sealed class ProviderAuthOptions
{
    public ProviderAuthType Type { get; set; } = ProviderAuthType.ApiKey;

    /// <summary>Nazwa nagłówka dla Type = ApiKey.</summary>
    public string HeaderName { get; set; } = "x-api-key";

    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>Opcjonalny scope w żądaniu tokena (OAuth2).</summary>
    public string? Scope { get; set; }

    public bool IsConfigured(string? apiKey) => Type switch
    {
        ProviderAuthType.None => true,
        ProviderAuthType.OAuth2 => Uri.TryCreate(TokenUrl, UriKind.Absolute, out _)
            && !string.IsNullOrWhiteSpace(ClientId)
            && !string.IsNullOrWhiteSpace(ClientSecret),
        ProviderAuthType.ApiKey => !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(HeaderName),
        _ => !string.IsNullOrWhiteSpace(apiKey)
    };
}

/// <summary>
/// Jedna operacja: metoda HTTP, ścieżka względna do BaseUrl i miejsce wyniku w odpowiedzi.
/// W ścieżce można użyć {carId}, {rentalId}, {offerId}, {email}, {location}, {startDate}, {endDate}, {age}, {licenseYears}.
/// </summary>
public sealed class ProviderOperation
{
    public string Method { get; set; } = "GET";

    public string Path { get; set; } = string.Empty;

    /// <summary>Gdzie w odpowiedzi jest wynik, np. "data" albo dla listy "data.items[]". Puste = cała odpowiedź.</summary>
    public string ResultPath { get; set; } = string.Empty;
}

/// <summary>Operacje z domyślnymi wartościami jak w naszym Car Rental API.</summary>
public sealed class ProviderOperationsOptions
{
    /// <summary>Lista aut.</summary>
    public ProviderOperation Cars { get; set; } = new() { Path = "api/v1/cars" };

    /// <summary>Jedno auto. Pusta ścieżka = szukamy auta na liście.</summary>
    public ProviderOperation Car { get; set; } = new() { Path = "api/v1/cars/{carId}" };

    /// <summary>Wycena. Dla GET dane idą tylko w ścieżce (placeholdery), dla POST w body.</summary>
    public ProviderOperation Offer { get; set; } = new() { Method = "POST", Path = "api/v1/offers" };

    /// <summary>Wniosek o wynajem.</summary>
    public ProviderOperation Rental { get; set; } = new() { Method = "POST", Path = "api/v1/rentals" };

    /// <summary>Status wynajmu.</summary>
    public ProviderOperation RentalStatus { get; set; } = new() { Path = "api/v1/rentals/{rentalId}" };

    /// <summary>Zgłoszenie zwrotu. Pusta ścieżka = wypożyczalnia tego nie obsługuje.</summary>
    public ProviderOperation Return { get; set; } = new() { Method = "POST", Path = "api/v1/rentals/{rentalId}/return" };
}

/// <summary>
/// Nazwy pól po stronie wypożyczalni. Ścieżki z kropkami ("price.daily", "items[0].id"),
/// wielkość liter nie ma znaczenia. Puste pole = wypożyczalnia go nie ma (bierzemy wartość domyślną albo nie wysyłamy).
/// </summary>
public sealed class ProviderMappingOptions
{
    public CarFieldMap Car { get; set; } = new();
    public OfferRequestFieldMap OfferRequest { get; set; } = new();
    public OfferFieldMap Offer { get; set; } = new();
    public RentalRequestFieldMap RentalRequest { get; set; } = new();
    public RentalFieldMap Rental { get; set; } = new();
    public ReturnRequestFieldMap ReturnRequest { get; set; } = new();

    /// <summary>Statusy wypożyczalni na nasze, np. "accepted": "Confirmed". Nasze nazwy działają bez wpisu.</summary>
    public Dictionary<string, string> Statuses { get; set; } = [];

    /// <summary>Waluta, gdy odpowiedź jej nie ma.</summary>
    public string DefaultCurrency { get; set; } = "PLN";

    /// <summary>Ważność oferty w minutach, gdy odpowiedź nie ma daty wygaśnięcia.</summary>
    public int OfferValidityMinutes { get; set; } = 10;

    /// <summary>Id złożone z samych cyfr wysyłamy w body jako liczbę (u nas carId to int).</summary>
    public bool NumericIds { get; set; } = true;
}

public sealed class CarFieldMap
{
    public string Id { get; set; } = "id";
    public string Make { get; set; } = "make";
    public string Model { get; set; } = "model";
    public string Year { get; set; } = "year";
    public string Location { get; set; } = "location";
    public string Available { get; set; } = "available";
}

public sealed class OfferRequestFieldMap
{
    public string CarId { get; set; } = "carId";
    public string Age { get; set; } = "age";
    public string LicenseYears { get; set; } = "licenseYears";
    public string Location { get; set; } = "location";
    public string StartDate { get; set; } = "startDate";
    public string EndDate { get; set; } = "endDate";
}

public sealed class OfferFieldMap
{
    public string OfferId { get; set; } = "offerId";
    public string CarId { get; set; } = "carId";
    public string DailyPrice { get; set; } = "dailyPrice";
    public string DailyInsurancePrice { get; set; } = "dailyInsurancePrice";
    public string TotalPrice { get; set; } = "totalPrice";
    public string Currency { get; set; } = "currency";
    public string StartDate { get; set; } = "startDate";
    public string EndDate { get; set; } = "endDate";
    public string Days { get; set; } = "days";
    public string ExpiresAt { get; set; } = "expiresAt";
}

public sealed class RentalRequestFieldMap
{
    public string OfferId { get; set; } = "offerId";
    public string FirstName { get; set; } = "firstName";
    public string LastName { get; set; } = "lastName";
    public string Email { get; set; } = "email";
}

public sealed class RentalFieldMap
{
    public string RentalId { get; set; } = "rentalId";
    public string Status { get; set; } = "status";
    public string Reason { get; set; } = "reason";
}

public sealed class ReturnRequestFieldMap
{
    public string Email { get; set; } = "email";
}
