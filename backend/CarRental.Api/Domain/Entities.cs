namespace CarRental.Api.Domain;

/// <summary>Status wynajmu, taki sam jak w kontrakcie (#26).</summary>
public enum RentalStatus
{
    Pending,
    Confirmed,
    Rejected,
    ReturnRequested,
    Completed
}

/// <summary>Samochód z floty naszej wypożyczalni.</summary>
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
}

/// <summary>Oferta dla konkretnego auta, kierowcy i terminu. Ważna co najmniej 10 minut.</summary>
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
}

/// <summary>Wniosek o wynajem złożony przez brokera na podstawie oferty.</summary>
public class Rental
{
    public Guid Id { get; set; }
    public Guid OfferId { get; set; }
    public Offer Offer { get; set; } = null!;
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal TotalPrice { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public RentalStatus Status { get; set; } = RentalStatus.Pending;

    /// <summary>Powód odrzucenia, gdy Status = Rejected.</summary>
    public string? Reason { get; set; }

    public string BrokerName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Zwrot
    public DateTime? ReturnRequestedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedBy { get; set; }
    public string? ReturnNote { get; set; }
    public string? AttachmentKey { get; set; }
    public string? AttachmentContentType { get; set; }
    public string? AttachmentFileName { get; set; }
}
