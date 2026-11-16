namespace CarRental.Api.Middleware;

/// <summary>
/// Nagłówki bezpieczeństwa (L4). API zwraca tylko JSON i pliki, więc CSP blokuje wszystko.
/// Swagger UI ma skrypty inline, więc jego strony CSP nie dostają. HSTS dodaje UseHsts.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        if (!context.Request.Path.StartsWithSegments("/swagger"))
        {
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(context);
    }
}
