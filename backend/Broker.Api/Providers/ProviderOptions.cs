using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Providers;

/// <summary>
/// Sekcja Providers: wspólny limit czasu i ustawienia naszej wypożyczalni (Providers:Our).
/// Pozostałe wypożyczalnie to sekcje Providers:&lt;Klucz&gt; z Type = Generic (patrz Generic/GenericProviderOptions.cs).
/// Klucze w chmurze idą do App Settings (Providers__Our__ApiKey), nie do repo.
/// </summary>
public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    /// <summary>Limit na całe odpytywanie wypożyczalni (wymaganie: wyniki w 15 s).</summary>
    [Range(0.05, 60)]
    public double SearchTimeoutSeconds { get; set; } = 15;

    public ProviderEndpointOptions Our { get; set; } = new() { DisplayName = "Car Rentals" };
}

/// <summary>Ustawienia jednej wypożyczalni: adres, klucz, limit czasu i to, czy jest włączona.</summary>
public class ProviderEndpointOptions
{
    /// <summary>
    /// Czy Broker ma pytać tę wypożyczalnię. To jedyne miejsce, gdzie się ją włącza.
    /// Puste: Our włączona, pozostałe wyłączone.
    /// </summary>
    public bool? Enabled { get; set; }

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
    public virtual bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out _) && !string.IsNullOrWhiteSpace(ApiKey);
}
