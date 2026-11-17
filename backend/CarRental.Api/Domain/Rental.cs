namespace CarRental.Api.Domain;

/// <summary>
/// Wniosek o wynajem złożony przez brokera na podstawie oferty.
/// Przejścia statusów: z Pending do Confirmed albo Rejected, z Confirmed do ReturnRequested, a potem do Completed.
/// Mapowanie do bazy: Data/Configurations/RentalConfiguration.cs.
/// </summary>
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

    /// <summary>Czy wynajem blokuje auto (czeka, potwierdzony albo w trakcie zwrotu).</summary>
    public bool BlocksCar => Status is RentalStatus.Pending or RentalStatus.Confirmed or RentalStatus.ReturnRequested;

    /// <summary>Zwrot można zgłosić tylko dla potwierdzonego wynajmu.</summary>
    public bool CanRequestReturn => Status == RentalStatus.Confirmed;

    /// <summary>Pracownik potwierdza zwrot dopiero po zgłoszeniu zwrotu przez klienta.</summary>
    public bool CanConfirmReturn => Status == RentalStatus.ReturnRequested;

    /// <summary>Czy podany e-mail to e-mail klienta z wniosku (bez względu na wielkość liter).</summary>
    public bool HasCustomerEmail(string email) =>
        string.Equals(Email, email.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Wynik procesu w tle: potwierdzenie, a gdy auta nie ma już we flocie, odrzucenie.</summary>
    public void Process(bool carIsActive, DateTime nowUtc) => Process(carIsActive, carIsFree: true, nowUtc);

    /// <summary>
    /// Wynik procesu w tle (Worker albo RentalConfirmationWorker): potwierdzenie albo odrzucenie,
    /// gdy auto wycofano z floty lub w tym terminie jest już inny wynajem.
    /// </summary>
    public void Process(bool carIsActive, bool carIsFree, DateTime nowUtc)
    {
        EnsureStatus(RentalStatus.Pending);

        if (!carIsActive)
        {
            Status = RentalStatus.Rejected;
            Reason = "Samochód został wycofany z floty.";
        }
        else if (!carIsFree)
        {
            Status = RentalStatus.Rejected;
            Reason = "Samochód jest już wynajęty w tym terminie.";
        }
        else
        {
            Status = RentalStatus.Confirmed;
        }

        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Klient zgłasza zwrot (przez brokera).</summary>
    public void RequestReturn(DateTime nowUtc)
    {
        EnsureStatus(RentalStatus.Confirmed);
        Status = RentalStatus.ReturnRequested;
        ReturnRequestedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Pracownik potwierdza zwrot: notatka o stanie auta i załącznik (zdjęcie albo PDF).</summary>
    public void CompleteReturn(string employeeName, string note, string attachmentKey, string contentType, string fileName, DateTime nowUtc)
    {
        EnsureStatus(RentalStatus.ReturnRequested);
        Status = RentalStatus.Completed;
        CompletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        CompletedBy = employeeName;
        ReturnNote = note.Trim();
        AttachmentKey = attachmentKey;
        AttachmentContentType = contentType;
        AttachmentFileName = fileName;
    }

    // Serwis sprawdza status wcześniej i zwraca 409, więc wyjątek oznacza błąd w kodzie, a nie w żądaniu
    private void EnsureStatus(RentalStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Niedozwolona zmiana statusu wynajmu {Id}: oczekiwano {expected}, jest {Status}.");
        }
    }
}
