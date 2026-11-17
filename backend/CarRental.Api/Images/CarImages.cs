using CarRental.Api.Options;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Images;

/// <summary>
/// Składa adres zdjęcia auta: w bazie (Car.ImageUrl) jest nazwa pliku z kontenera "cars",
/// a początek adresu (konto Blob Storage) jest w konfiguracji Images:BaseUrl. Dzięki temu seed nie ma twardej domeny.
/// </summary>
public sealed class CarImages(IOptionsMonitor<ImageOptions> options)
{
    /// <summary>Pełny adres zdjęcia albo null (brak pliku albo brak Images:BaseUrl).</summary>
    public string? UrlFor(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        // Pełny adres w bazie zostawiamy jak jest (np. zdjęcie spoza naszego konta)
        if (Uri.TryCreate(stored, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
        {
            return absolute.ToString();
        }

        var baseUrl = options.CurrentValue.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var path = string.Join('/', stored.Trim().TrimStart('/').Split('/').Select(Uri.EscapeDataString));
        return $"{baseUrl.TrimEnd('/')}/{path}";
    }

    /// <summary>Autor i licencja zdjęcia z Images:Credits (klucz to nazwa pliku) albo null.</summary>
    public string? CreditFor(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var fileName = stored.Split('/').Last();
        return options.CurrentValue.Credits.TryGetValue(fileName, out var credit) && !string.IsNullOrWhiteSpace(credit)
            ? credit
            : null;
    }
}
