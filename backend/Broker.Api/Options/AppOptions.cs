using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Options;

/// <summary>Cache katalogu aut. Wymaganie: katalog można trzymać najwyżej 30 minut.</summary>
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    [Range(1, 30)]
    public int CacheMinutes { get; set; } = 10;
}

/// <summary>Sprzątanie starych wycen w tle (część punktu "clear data").</summary>
public sealed class QuoteCleanupOptions
{
    public const string SectionName = "Quotes";

    public bool CleanupEnabled { get; set; } = true;

    [Range(1, 1440)]
    public int CleanupIntervalMinutes { get; set; } = 10;

    /// <summary>Ile godzin po wygaśnięciu trzymamy ofertę.</summary>
    [Range(1, 24 * 30)]
    public int RetentionHours { get; set; } = 24;
}

/// <summary>Ustawienia logowania. DevLogin działa tylko w Development i tylko gdy nie ma Entra.</summary>
public sealed class BrokerAuthOptions
{
    public const string SectionName = "Auth";

    public bool UseDevLogin { get; set; }
}
