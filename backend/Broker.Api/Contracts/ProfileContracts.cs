using System.ComponentModel.DataAnnotations;

namespace Broker.Api.Contracts;

/// <summary>Profil zalogowanego użytkownika. Wiek i staż są liczone na dziś.</summary>
public sealed record ProfileResponse(
    string FirstName,
    string LastName,
    string Email,
    DateOnly? DateOfBirth,
    DateOnly? LicenseObtainedOn,
    string? Location,
    int? Age,
    int? LicenseYears,
    bool IsComplete,
    IReadOnlyList<string> MissingFields,
    DateTime UpdatedAt);

/// <summary>Zapis profilu (PUT /api/v1/profile). Pole null = wyczyszczone.</summary>
public sealed class UpdateProfileRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string? FirstName { get; init; }

    [Required, StringLength(100, MinimumLength = 1)]
    public string? LastName { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public DateOnly? LicenseObtainedOn { get; init; }

    [StringLength(100, MinimumLength = 2)]
    public string? Location { get; init; }
}
