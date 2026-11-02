using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Auth;

/// <summary>
/// Zastępcze logowanie pracownika TYLKO do pracy lokalnej i testów,
/// dopóki aplikacja nie jest zarejestrowana w Entra ID (#58).
/// Rejestrowane wyłącznie w środowisku Development/Testing i tylko gdy nie ma AzureAd:ClientId.
/// Przykład: nagłówki X-Dev-Employee: anna, X-Dev-Roles: Employee
/// </summary>
public sealed class DevEmployeeAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var name = Request.Headers[AuthConstants.DevEmployeeHeader].ToString();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, name) };

        var roles = Request.Headers[AuthConstants.DevRolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
