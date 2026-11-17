namespace Broker.Api.Contracts;

/// <summary>Jak działa logowanie w tej instancji Brokera.</summary>
public enum AuthMode
{
    Entra,
    DevLogin,
    None
}

/// <summary>Stan logowania (GET /api/v1/auth/me).</summary>
public sealed record MeResponse(
    bool IsAuthenticated,
    string? Name,
    string? Email,
    IReadOnlyList<string> Roles,
    bool IsEmployee,
    bool ProfileComplete,
    AuthMode AuthMode);
