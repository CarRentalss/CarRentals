using System.Security.Claims;

namespace Broker.Api.Auth;

/// <summary>Zalogowany użytkownik (dane z claimów ciasteczka sesji).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>oid z Entra ID (albo "dev-{login}" przy dev-login).</summary>
    string? ObjectId { get; }

    /// <summary>Login (Identity.Name), np. anna. Idzie do Car Rental API jako nazwa pracownika w trybie dev.</summary>
    string? UserName { get; }

    /// <summary>Nazwa do wyświetlenia, np. "Anna Nowak".</summary>
    string? DisplayName { get; }

    string? Email { get; }
    string? FirstName { get; }
    string? LastName { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsEmployee { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public string? ObjectId => IsAuthenticated ? User!.GetObjectId() : null;

    public string? UserName => IsAuthenticated ? User!.Identity!.Name : null;

    public string? DisplayName => IsAuthenticated ? User!.GetDisplayName() : null;

    public string? Email => IsAuthenticated ? User!.GetEmail() : null;

    public string? FirstName => IsAuthenticated ? User!.GetFirstName() : null;

    public string? LastName => IsAuthenticated ? User!.GetLastName() : null;

    public IReadOnlyList<string> Roles => IsAuthenticated ? User!.GetRoles() : [];

    public bool IsEmployee => IsAuthenticated && User!.IsEmployee();
}

/// <summary>
/// Odczyt claimów. Entra przez OIDC daje claimy krótkie ("oid", "roles") albo zmapowane na długie
/// (ClaimTypes.*), zależnie od ustawień handlera, więc sprawdzamy oba warianty.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public const string ObjectIdClaim = "oid";
    public const string ObjectIdLongClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    public const string NameClaim = "name";
    public const string RolesClaim = "roles";

    public static string? GetObjectId(this ClaimsPrincipal user) =>
        First(user, ObjectIdClaim, ObjectIdLongClaim);

    public static string? GetDisplayName(this ClaimsPrincipal user) =>
        First(user, NameClaim) ?? user.Identity?.Name;

    public static string? GetEmail(this ClaimsPrincipal user) =>
        First(user, "email", ClaimTypes.Email, "preferred_username", ClaimTypes.Upn);

    public static string? GetFirstName(this ClaimsPrincipal user) =>
        First(user, "given_name", ClaimTypes.GivenName) ?? SplitName(user.GetDisplayName()).First;

    public static string? GetLastName(this ClaimsPrincipal user) =>
        First(user, "family_name", ClaimTypes.Surname) ?? SplitName(user.GetDisplayName()).Last;

    public static IReadOnlyList<string> GetRoles(this ClaimsPrincipal user) =>
        user.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == RolesClaim)
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool IsEmployee(this ClaimsPrincipal user) =>
        user.GetRoles().Contains(AuthSetup.EmployeeRole, StringComparer.Ordinal);

    private static string? First(ClaimsPrincipal user, params string[] types)
    {
        foreach (var type in types)
        {
            var value = user.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    // Entra bez opcjonalnych claimów daje tylko "name" (np. "Anna Nowak"), wtedy dzielimy go na imię i nazwisko
    private static (string? First, string? Last) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null);
        }

        var parts = name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (parts[0], null);
    }
}
