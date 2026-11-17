namespace Broker.Api.Domain;

/// <summary>
/// Wypożyczalnia podłączona do Brokera. W bazie jest tylko kod i nazwa (do kluczy obcych w Quotes i Rentals).
/// Adresy, klucze i włączenie (Providers:&lt;Klucz&gt;:Enabled) są w konfiguracji (App Settings), nie w bazie.
/// </summary>
public class Provider
{
    public const string OurCode = "our";
    public const string LecturerCode = "lecturer";
    public const string OtherGroupCode = "other-group";

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Tylko podgląd: ProviderRegistry przepisuje tu stan z konfiguracji. Zmiana w bazie niczego nie włącza.</summary>
    public bool IsEnabled { get; set; }

    public List<Quote> Quotes { get; set; } = [];
}
