using Broker.Api.Auth;
using Broker.Api.Contracts;
using Broker.Api.Data;
using Broker.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Broker.Api.Services;

/// <summary>Profil zalogowanego użytkownika, szukany po oid z tokena.</summary>
public sealed class ProfileService(BrokerDbContext db, ICurrentUser currentUser, TimeProvider clock)
{
    private const int MinDriverAge = 18;
    private const int MinLicenseAge = 16;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Profil bieżącego użytkownika bez tworzenia (null, jak go jeszcze nie ma albo nikt nie jest zalogowany).</summary>
    public async Task<UserProfile?> FindCurrentAsync(CancellationToken cancellationToken)
    {
        var objectId = currentUser.ObjectId;
        if (string.IsNullOrEmpty(objectId))
        {
            return null;
        }

        return await db.UserProfiles.FirstOrDefaultAsync(u => u.EntraObjectId == objectId, cancellationToken);
    }

    /// <summary>Profil bieżącego użytkownika; przy pierwszym wejściu tworzy go z danych z konta.</summary>
    public async Task<ServiceResult<UserProfile>> GetOrCreateCurrentAsync(CancellationToken cancellationToken)
    {
        var objectId = currentUser.ObjectId;
        if (!currentUser.IsAuthenticated || string.IsNullOrEmpty(objectId))
        {
            return ServiceError.Unauthorized("Zaloguj się.");
        }

        var profile = await FindCurrentAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        profile = new UserProfile
        {
            Id = Guid.NewGuid(),
            EntraObjectId = objectId,
            Email = Truncate(currentUser.Email, 254),
            FirstName = Truncate(currentUser.FirstName, 100),
            LastName = Truncate(currentUser.LastName, 100),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.UserProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Dwa równoległe pierwsze żądania: drugie trafi na unikalny indeks EntraObjectId, bierzemy zapisany profil
            db.Entry(profile).State = EntityState.Detached;
            var existing = await FindCurrentAsync(cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }

        return profile;
    }

    public async Task<ServiceResult<ProfileResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var result = await GetOrCreateCurrentAsync(cancellationToken);
        return result.IsSuccess ? ToResponse(result.Value) : result.Error;
    }

    public async Task<ServiceResult<ProfileResponse>> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var today = Today;
        if (request.DateOfBirth is { } birth)
        {
            if (birth > today)
            {
                return ServiceError.Validation(UserProfile.DateOfBirthField, "Data urodzenia nie może być w przyszłości.");
            }

            if (UserProfile.FullYearsBetween(birth, today) < MinDriverAge)
            {
                return ServiceError.Validation(UserProfile.DateOfBirthField, $"Kierowca musi mieć co najmniej {MinDriverAge} lat.");
            }
        }

        if (request.LicenseObtainedOn is { } license)
        {
            if (license > today)
            {
                return ServiceError.Validation(UserProfile.LicenseObtainedOnField, "Data uzyskania prawa jazdy nie może być w przyszłości.");
            }

            if (request.DateOfBirth is { } dob && license < dob.AddYears(MinLicenseAge))
            {
                return ServiceError.Validation(UserProfile.LicenseObtainedOnField, $"Prawo jazdy można mieć najwcześniej od {MinLicenseAge}. urodzin.");
            }
        }

        var result = await GetOrCreateCurrentAsync(cancellationToken);
        if (!result.IsSuccess)
        {
            return result.Error;
        }

        var profile = result.Value;
        profile.FirstName = request.FirstName!.Trim();
        profile.LastName = request.LastName!.Trim();
        profile.Email = request.Email!.Trim();
        profile.DateOfBirth = request.DateOfBirth;
        profile.LicenseObtainedOn = request.LicenseObtainedOn;
        profile.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        profile.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(profile);
    }

    public ProfileResponse ToResponse(UserProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var today = Today;
        return new ProfileResponse(
            profile.FirstName,
            profile.LastName,
            profile.Email,
            profile.DateOfBirth,
            profile.LicenseObtainedOn,
            profile.Location,
            profile.GetAge(today),
            profile.GetLicenseYears(today),
            profile.IsComplete,
            profile.MissingFields,
            DateTime.SpecifyKind(profile.UpdatedAtUtc, DateTimeKind.Utc));
    }

    private static string Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
