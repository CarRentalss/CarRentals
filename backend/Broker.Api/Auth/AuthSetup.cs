using Broker.Api.Contracts;
using Broker.Api.Options;
using Broker.Api.Providers;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace Broker.Api.Auth;

/// <summary>Jaki tryb logowania wybrano przy starcie (singleton, czyta go np. /auth/me).</summary>
public sealed record AuthModeInfo(AuthMode Mode, string? EmployeeApiScope);

/// <summary>
/// Logowanie w Brokerze (BFF). Entra ID przez Microsoft.Identity.Web (OIDC + ciasteczko HttpOnly),
/// gdy jest AzureAd:ClientId. Bez Entra, tylko lokalnie: dev-login (ciasteczko dla podanego użytkownika).
/// Front nigdy nie dostaje tokenów, ma tylko ciasteczko.
/// </summary>
public static class AuthSetup
{
    public const string EmployeePolicy = "Employee";
    public const string EmployeeRole = "Employee";
    public const string CookieName = "CarRentals.Session";

    /// <summary>Schemat OIDC dodawany przez Microsoft.Identity.Web (OpenIdConnectDefaults.AuthenticationScheme).</summary>
    public const string OpenIdConnectScheme = "OpenIdConnect";

    public static AuthMode AddBrokerAuthentication(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;
        var isLocal = environment.IsDevelopment() || environment.IsEnvironment("Testing");

        // Wyzwanie (brak sesji) idzie do ciasteczka, a nie do Entra: dla /api chcemy 401, nie przekierowania.
        // Do Entra przekierowuje tylko /api/v1/auth/login.
        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });

        AuthMode mode;
        string? employeeScope = null;
        var clientId = configuration["AzureAd:ClientId"];

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            // Scope do Car Rental API (panel pracownika). Jedna rejestracja aplikacji: api://{clientId}/access_as_user
            employeeScope = configuration[$"{ProvidersOptions.SectionName}:Our:Scope"];
            if (string.IsNullOrWhiteSpace(employeeScope))
            {
                employeeScope = $"api://{clientId}/access_as_user";
            }

            // AddMicrosoftIdentityWebApp sam dodaje schemat ciasteczka "Cookies"
            authentication
                .AddMicrosoftIdentityWebApp(configuration.GetSection("AzureAd"))
                .EnableTokenAcquisitionToCallDownstreamApi(new[] { employeeScope })
                .AddInMemoryTokenCaches();

            mode = AuthMode.Entra;
        }
        else
        {
            authentication.AddCookie();
            var useDevLogin = configuration.GetValue($"{BrokerAuthOptions.SectionName}:UseDevLogin", false);
            // Dev-login w chmurze byłby dziurą (każdy mógłby się podać za pracownika), więc tylko lokalnie
            mode = isLocal && useDevLogin ? AuthMode.DevLogin : AuthMode.None;
        }

        services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Lokalnie Vite i Broker chodzą po http, w chmurze tylko https
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;

            // API nie przekierowuje na strony logowania, tylko zwraca kod (front sam decyduje, co pokazać)
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddSingleton(new AuthModeInfo(mode, employeeScope));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddAuthorizationBuilder()
            .AddPolicy(EmployeePolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.IsEmployee()))
            // Wszystko bez [AllowAnonymous] wymaga zalogowania (deny by default)
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        return mode;
    }

    /// <summary>Adres powrotu po logowaniu: tylko lokalny (bez open redirect).</summary>
    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var isLocal = returnUrl.StartsWith('/')
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);

        return isLocal ? returnUrl : "/";
    }
}
