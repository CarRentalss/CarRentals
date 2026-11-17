namespace Broker.Api.Middleware;

/// <summary>
/// Nagłówki bezpieczeństwa (L4: X-Content-Type-Options, X-Frame-Options, Referrer-Policy, CSP). HSTS dodaje UseHsts.
/// CSP pasuje do zbudowanego Reacta (same pliki z /assets, bez skryptów inline i zewnętrznych adresów).
/// Wyjątek: zdjęcia aut z Azure Blob Storage (img-src https://*.blob.core.windows.net).
/// Swagger UI ma skrypty inline, więc jego strony CSP nie dostają.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://*.blob.core.windows.net; " +
        "font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        }

        return next(context);
    }
}
