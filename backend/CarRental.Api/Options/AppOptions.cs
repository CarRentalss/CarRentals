using System.ComponentModel.DataAnnotations;

namespace CarRental.Api.Options;

/// <summary>Zasady wyceny: cena bazowa razy mnożniki za młodego kierowcę i krótki staż.</summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    [Range(18, 100)]
    public int YoungDriverAgeLimit { get; set; } = 25;

    [Range(typeof(decimal), "1", "5")]
    public decimal YoungDriverMultiplier { get; set; } = 1.25m;

    [Range(1, 50)]
    public int NewDriverYearsLimit { get; set; } = 3;

    [Range(typeof(decimal), "1", "5")]
    public decimal NewDriverMultiplier { get; set; } = 1.20m;

    [Range(1, 365)]
    public int MaxRentalDays { get; set; } = 30;

    [Required, StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "PLN";
}

/// <summary>Ważność oferty. Wymaganie: co najmniej 10 minut.</summary>
public sealed class OfferOptions
{
    public const string SectionName = "Offers";

    [Range(10, 1440)]
    public int ValidityMinutes { get; set; } = 10;
}

/// <summary>
/// Potwierdzanie wynajmów w tle. Wynajem nie jest potwierdzany od razu (status Pending),
/// tylko po chwili przez proces w tle, tak jak w prawdziwej wypożyczalni.
/// </summary>
public sealed class RentalProcessingOptions
{
    public const string SectionName = "RentalProcessing";

    public bool Enabled { get; set; } = true;

    [Range(0, 3600)]
    public int ConfirmationDelaySeconds { get; set; } = 10;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Gdzie zapisujemy załączniki. Bez connection stringa do Bloba: dysk (LocalPath).
/// Na Azure: Storage:BlobConnectionString (albo ConnectionStrings:Blob) i prywatny kontener BlobContainer.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string LocalPath { get; set; } = "App_Data/attachments";

    public string? BlobConnectionString { get; set; }

    [Required, RegularExpression("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", ErrorMessage = "Nazwa kontenera: 3-63 znaki, małe litery, cyfry i '-'.")]
    public string BlobContainer { get; set; } = "attachments";
}

/// <summary>
/// Zdjęcia aut. W bazie jest sama nazwa pliku (np. toyota-corolla-2022.jpg), pełny adres składamy z BaseUrl,
/// np. https://stcarrentalsjkm.blob.core.windows.net/cars/. Bez BaseUrl API zwraca imageUrl = null (front pokazuje zaślepkę).
/// </summary>
public sealed class ImageOptions
{
    public const string SectionName = "Images";

    public string? BaseUrl { get; set; }

    /// <summary>Autor i licencja zdjęcia (Wikimedia Commons), klucz to nazwa pliku.</summary>
    public Dictionary<string, string> Credits { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsValid() =>
        string.IsNullOrWhiteSpace(BaseUrl)
        || (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp));
}
