namespace CarRental.Api.Domain;

/// <summary>Samochód z floty naszej wypożyczalni. Mapowanie do bazy: Data/Configurations/CarConfiguration.cs.</summary>
public class Car
{
    public int Id { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Location { get; set; } = string.Empty;
    public decimal BasePricePerDay { get; set; }
    public decimal InsurancePricePerDay { get; set; }

    /// <summary>Czy auto jest we flocie (wycofane auta nie są pokazywane).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Czy auto można odebrać w podanym mieście (wielkość liter nie ma znaczenia).</summary>
    public bool IsAvailableIn(string location) =>
        string.Equals(Location, location.Trim(), StringComparison.OrdinalIgnoreCase);
}
