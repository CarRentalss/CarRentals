namespace CarRental.Api.Domain;

/// <summary>
/// Oferta dla konkretnego auta, kierowcy i terminu. Ważna co najmniej 10 minut.
/// Mapowanie do bazy: Data/Configurations/OfferConfiguration.cs.
/// </summary>
public class Offer
{
    public Guid Id { get; set; }
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;

    public int DriverAge { get; set; }
    public int LicenseYears { get; set; }
    public string Location { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal DailyPrice { get; set; }
    public decimal DailyInsurancePrice { get; set; }
    public decimal TotalPrice { get; set; }
    public string Currency { get; set; } = "PLN";

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Który broker poprosił o ofertę (z klucza API).</summary>
    public string BrokerName { get; set; } = string.Empty;

    /// <summary>Oferta wygasła, gdy minął jej termin ważności (dokładnie w chwili ExpiresAtUtc jest jeszcze ważna).</summary>
    public bool IsExpired(DateTime nowUtc) => nowUtc > ExpiresAtUtc;

    /// <summary>Tworzy wniosek o wynajem na warunkach z oferty (cena i termin są kopiowane, to "zdjęcie" warunków).</summary>
    public Rental CreateRental(string firstName, string lastName, string email, DateTime nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        OfferId = Id,
        CarId = CarId,
        StartDate = StartDate,
        EndDate = EndDate,
        TotalPrice = TotalPrice,
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        Email = email.Trim(),
        Status = RentalStatus.Pending,
        BrokerName = BrokerName,
        CreatedAtUtc = nowUtc,
        UpdatedAtUtc = nowUtc
    };
}
