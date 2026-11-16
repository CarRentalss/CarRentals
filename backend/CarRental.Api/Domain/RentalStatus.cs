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
