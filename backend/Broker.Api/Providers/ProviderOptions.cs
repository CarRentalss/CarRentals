using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Providers;

/// <summary>
/// Sekcja Providers: wspólny limit czasu i ustawienia każdej wypożyczalni (adres, klucz).
/// Klucze w chmurze idą do App Settings (Providers__Our__ApiKey), nie do repo.
/// </summary>
public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    /// <summary>Limit na całe odpytywanie wypożyczalni (wymaganie: wyniki w 15 s).</summary>
    [Range(0.05, 60)]
    public double SearchTimeoutSeconds { get; set; } = 15;

    public ProviderEndpointOptions Our { get; set; } = new() { DisplayName = "Car Rentals" };

    /// <summary>KM3: API prowadzącego.</summary>
    public ProviderEndpointOptions Lecturer { get; set; } = new() { DisplayName = "Wypożyczalnia prowadzącego" };

    /// <summary>KM3: API innej grupy.</summary>
    public ProviderEndpointOptions OtherGroup { get; set; } = new() { DisplayName = "Wypożyczalnia innej grupy" };
}

public sealed class ProviderEndpointOptions
{
    public string DisplayName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Scope do tokena pracownika (panel pracownika), np. api://{clientId}/access_as_user.
    /// Puste = liczony z AzureAd:ClientId.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>Dostawca jest skonfigurowany, jak ma poprawny adres i klucz.</summary>
    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(ApiKey);
}
