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

/// <summary>Gdzie zapisujemy załączniki. Lokalnie dysk, w KM3 podmienimy na Azure Blob Storage.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string LocalPath { get; set; } = "App_Data/attachments";
}
