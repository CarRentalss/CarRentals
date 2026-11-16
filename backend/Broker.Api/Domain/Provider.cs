namespace Broker.Api.Domain;

/// <summary>
/// Wypożyczalnia podłączona do Brokera. W bazie jest tylko kod, nazwa i to, czy jest włączona.
/// Adresy i klucze API są w konfiguracji (App Settings), nie w bazie.
/// </summary>
public class Provider
{
    public const string OurCode = "our";
    public const string LecturerCode = "lecturer";
    public const string OtherGroupCode = "other-group";

    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }

    public List<Quote> Quotes { get; set; } = [];
}
