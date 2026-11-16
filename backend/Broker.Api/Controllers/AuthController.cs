using System.Security.Claims;
using System.Text.RegularExpressions;
using Broker.Api.Auth;
using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>Logowanie (BFF): Entra ID albo lokalnie dev-login. Front ma tylko ciasteczko HttpOnly.</summary>
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed partial class AuthController(
    AuthModeInfo authMode,
    ICurrentUser currentUser,
    ProfileService profiles) : ApiControllerBase
{
    private const string DefaultDevUser = "jan.kowalski";

    /// <summary>Start logowania. Nawigacja przeglądarki (nie fetch), po zalogowaniu powrót na lokalny returnUrl.</summary>
    /// <param name="returnUrl">Lokalny adres, np. /profile. Inny (np. https://...) jest zamieniany na /.</param>
    [HttpGet("login")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        var safeReturnUrl = AuthSetup.SafeReturnUrl(returnUrl);

        return authMode.Mode switch
        {
            AuthMode.Entra => Challenge(new AuthenticationProperties { RedirectUri = safeReturnUrl }, AuthSetup.OpenIdConnectScheme),
            AuthMode.DevLogin => Redirect($"/api/v1/auth/dev-login?name={DefaultDevUser}&returnUrl={Uri.EscapeDataString(safeReturnUrl)}"),
            _ => Problem(title: "Logowanie nie jest skonfigurowane.", statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    /// <summary>
    /// Tylko lokalnie (Development + Auth:UseDevLogin): logowanie bez hasła jako podany użytkownik,
    /// np. ?name=anna&amp;role=Employee&amp;returnUrl=/employee/rentals. W chmurze zwraca 404.
    /// </summary>
    [HttpGet("dev-login")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DevLogin(
        [FromQuery] string? name, [FromQuery] string? role, [FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        if (authMode.Mode != AuthMode.DevLogin)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(name) || !DevNameRegex().IsMatch(name))
        {
            return ErrorResult(ServiceError.Validation("name", "Podaj login: 2-50 znaków (litery, cyfry, kropka, myślnik, podkreślnik)."));
        }

        var roles = (role ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => DevNameRegex().IsMatch(r))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var claims = new List<Claim>
        {
            new(ClaimsPrincipalExtensions.ObjectIdClaim, $"dev-{name}"),
            new(ClaimTypes.Name, name),
            new(ClaimsPrincipalExtensions.NameClaim, name),
            new(ClaimTypes.Email, $"{name}@dev.local"),
            new(ClaimTypes.GivenName, name),
            new(ClaimTypes.Surname, "Dev")
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "DevLogin"));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            return LocalRedirect(AuthSetup.SafeReturnUrl(returnUrl));
        }

        // W tym żądaniu ciasteczka jeszcze nie ma, więc /me liczymy od nowego użytkownika
        HttpContext.User = principal;
        return Ok(await BuildMeAsync(cancellationToken));
    }

    /// <summary>Wylogowanie z Brokera (usuwa ciasteczko). Wymaga X-CSRF: 1.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Stan logowania, role i czy profil jest kompletny.</summary>
    [HttpGet("me")]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Me(CancellationToken cancellationToken) =>
        Ok(await BuildMeAsync(cancellationToken));

    private async Task<MeResponse> BuildMeAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return new MeResponse(false, null, null, [], false, false, authMode.Mode);
        }

        var profile = await profiles.FindCurrentAsync(cancellationToken);
        return new MeResponse(
            true,
            currentUser.DisplayName,
            profile?.Email ?? currentUser.Email,
            currentUser.Roles,
            currentUser.IsEmployee,
            profile?.IsComplete ?? false,
            authMode.Mode);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{2,50}$")]
    private static partial Regex DevNameRegex();
}
