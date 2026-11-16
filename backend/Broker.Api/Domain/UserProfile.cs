namespace Broker.Api.Domain;

/// <summary>
/// Profil klienta w Brokerze. Trzymamy daty (urodzenia i prawa jazdy), a nie wiek i staż,
/// bo te zmieniają się z czasem. Wiek i staż liczymy na dany dzień.
/// </summary>
public class UserProfile
{
    public const string FirstNameField = "firstName";
    public const string LastNameField = "lastName";
    public const string EmailField = "email";
    public const string DateOfBirthField = "dateOfBirth";
    public const string LicenseObtainedOnField = "licenseObtainedOn";
    public const string LocationField = "location";

    public Guid Id { get; set; }

    /// <summary>Identyfikator użytkownika z Entra ID (claim oid). Lokalnie przy dev-login "dev-{login}".</summary>
    public string EntraObjectId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? LicenseObtainedOn { get; set; }
    public string? Location { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public List<Quote> Quotes { get; set; } = [];

    /// <summary>Wiek w pełnych latach na dany dzień albo null, jak nie ma daty urodzenia.</summary>
    public int? GetAge(DateOnly today) => DateOfBirth is { } birth ? FullYearsBetween(birth, today) : null;

    /// <summary>Staż prawa jazdy w pełnych latach na dany dzień albo null, jak nie ma daty.</summary>
    public int? GetLicenseYears(DateOnly today) =>
        LicenseObtainedOn is { } obtained ? FullYearsBetween(obtained, today) : null;

    /// <summary>Pola potrzebne do wyceny i wynajmu, których jeszcze brakuje (nazwy jak w JSON).</summary>
    public IReadOnlyList<string> MissingFields
    {
        get
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(FirstName)) missing.Add(FirstNameField);
            if (string.IsNullOrWhiteSpace(LastName)) missing.Add(LastNameField);
            if (string.IsNullOrWhiteSpace(Email)) missing.Add(EmailField);
            if (DateOfBirth is null) missing.Add(DateOfBirthField);
            if (LicenseObtainedOn is null) missing.Add(LicenseObtainedOnField);
            if (string.IsNullOrWhiteSpace(Location)) missing.Add(LocationField);
            return missing;
        }
    }

    public bool IsComplete => MissingFields.Count == 0;

    /// <summary>Pełne lata między dwiema datami (0, jak "do" jest wcześniej niż "od").</summary>
    public static int FullYearsBetween(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }

        var years = to.Year - from.Year;
        // Rocznica jeszcze nie minęła w tym roku (29 lutego liczymy jak 1 marca w latach nieprzestępnych)
        if (to.Month < from.Month || (to.Month == from.Month && to.Day < from.Day))
        {
            years--;
        }

        return years;
    }
}
