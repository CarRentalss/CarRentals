using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Contracts;

// Lustrzane do kontraktów Car Rental API (panel pracownika idzie przez Brokera).

/// <summary>Status wynajmu w naszej wypożyczalni.</summary>
public enum RentalStatus
{
    Pending,
    Confirmed,
    Rejected,
    ReturnRequested,
    Completed
}

public sealed record EmployeeRentalListItem(
    Guid RentalId,
    int CarId,
    string Car,
    string CustomerName,
    string Email,
    DateOnly StartDate,
    DateOnly EndDate,
    RentalStatus Status,
    decimal TotalPrice);

public sealed record EmployeeRentalDetails(
    Guid RentalId,
    int CarId,
    string Car,
    string Location,
    string FirstName,
    string LastName,
    string Email,
    DateOnly StartDate,
    DateOnly EndDate,
    RentalStatus Status,
    string? Reason,
    decimal TotalPrice,
    string BrokerName,
    DateTime CreatedAt,
    DateTime? ReturnRequestedAt,
    DateTime? CompletedAt,
    string? CompletedBy,
    string? ReturnNote,
    bool HasAttachment);

public sealed record RentalStatusChangedResponse(Guid RentalId, RentalStatus Status);

/// <summary>Formularz potwierdzenia zwrotu (multipart/form-data), przekazywany do Car Rental API.</summary>
public sealed class ConfirmReturnForm
{
    /// <summary>Krótka notatka o stanie auta.</summary>
    [Required, StringLength(1000, MinimumLength = 3)]
    public string? Note { get; init; }

    /// <summary>Zdjęcie (jpg, png) albo PDF, do 10 MB.</summary>
    [Required]
    public IFormFile? File { get; init; }
}

/// <summary>Plik pobrany z Car Rental API.</summary>
public sealed record AttachmentFile(byte[] Content, string ContentType, string FileName);
