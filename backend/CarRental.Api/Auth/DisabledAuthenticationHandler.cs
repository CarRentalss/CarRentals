using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CarRental.Api.Auth;

/// <summary>
/// Schemat "zamknięte": nigdy nikogo nie uwierzytelnia (zawsze NoResult), więc wyzwanie kończy się 401.
/// Używany dla pracowników poza Development, gdy nie ma konfiguracji Entra ID (AzureAd).
/// </summary>
public sealed class DisabledAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(AuthenticateResult.NoResult());
}
