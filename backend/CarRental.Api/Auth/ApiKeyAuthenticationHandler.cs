using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Auth;

/// <summary>
/// Uwierzytelnianie brokera po kluczu w nagłówku x-api-key.
/// Każdy broker ma swój klucz, więc wiemy, kto pyta, i możemy wyłączyć jeden klucz bez ruszania drugiego.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<ApiKeyClientsOptions> clients)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthConstants.ApiKeyHeader, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedKey = values.ToString();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return Task.FromResult(AuthenticateResult.Fail("Pusty klucz API."));
        }

        var client = FindClient(providedKey);
        if (client is null)
        {
            // Samego klucza nie logujemy
            return Task.FromResult(AuthenticateResult.Fail("Nieprawidłowy klucz API."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, client.Name),
            new Claim(AuthConstants.BrokerClaim, client.Name)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private ApiKeyClient? FindClient(string providedKey)
    {
        var provided = Encoding.UTF8.GetBytes(providedKey);
        ApiKeyClient? match = null;

        // Porównanie w stałym czasie, żeby po czasie odpowiedzi nie dało się zgadywać klucza
        foreach (var client in clients.CurrentValue.Clients)
        {
            var expected = Encoding.UTF8.GetBytes(client.Key);
            if (CryptographicOperations.FixedTimeEquals(provided, expected))
            {
                match = client;
            }
        }

        return match;
    }
}
