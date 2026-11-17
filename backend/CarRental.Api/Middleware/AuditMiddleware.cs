using System.Diagnostics;
using System.Text;
using CarRental.Api.Auth;

namespace CarRental.Api.Middleware;

/// <summary>
/// Audyt wywołań API: kto (broker albo pracownik), co (metoda, ścieżka), z jakim wynikiem i jak długo.
/// Logujemy tylko nagłówki z listy dozwolonych. Nigdy x-api-key, Authorization ani Cookie.
/// Query stringa nie logujemy (mogą być dane osobowe). Treść JSON do 4 KB z maskowaniem e-maila, imienia i nazwiska.
/// Gdy jest APPLICATIONINSIGHTS_CONNECTION_STRING, te logi trafiają też do Application Insights (OpenTelemetry).
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    /// <summary>Największa treść żądania, którą wpisujemy do audytu.</summary>
    public const int MaxLoggedBodyBytes = 4 * 1024;

    /// <summary>Nagłówki, które wolno zapisać w logu.</summary>
    public static readonly IReadOnlyList<string> AllowedHeaders =
        ["User-Agent", "Content-Type", "Content-Length", "X-Correlation-Id", "traceparent"];

    /// <summary>Nagłówki z sekretami. Nie trafiają do logu nawet wtedy, gdyby ktoś dopisał je do listy dozwolonych.</summary>
    public static readonly IReadOnlySet<string> ForbiddenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AuthConstants.ApiKeyHeader, "Authorization", "Cookie", "Set-Cookie", "Proxy-Authorization"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        var body = await ReadJsonBodyAsync(context.Request);

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
            var caller = context.User.FindFirst(AuthConstants.BrokerClaim)?.Value
                ?? context.User.Identity?.Name
                ?? "anonymous";

            logger.LogInformation(
                "Audit {Method} {Path} -> {StatusCode} in {ElapsedMs} ms, caller: {Caller}, headers: {Headers}, body: {Body}, traceId: {TraceId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                caller,
                FormatAllowedHeaders(context.Request.Headers),
                body ?? "-",
                Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier);
        }
    }

    /// <summary>Nagłówki z listy dozwolonych w postaci "Nazwa=wartość; ...".</summary>
    public static string FormatAllowedHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var parts = new List<string>();
        foreach (var name in AllowedHeaders)
        {
            if (ForbiddenHeaders.Contains(name))
            {
                continue;
            }

            if (headers.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value.ToString()))
            {
                parts.Add($"{name}={value}");
            }
        }

        return parts.Count == 0 ? "-" : string.Join("; ", parts);
    }

    // Czytamy tylko mały JSON (bez plików multipart), a potem cofamy strumień, żeby kontroler dostał całą treść
    private static async Task<string?> ReadJsonBodyAsync(HttpRequest request)
    {
        if (!request.HasJsonContentType()
            || request.ContentLength is null or 0
            || request.ContentLength > MaxLoggedBodyBytes)
        {
            return null;
        }

        request.EnableBuffering();
        string text;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            text = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
        }

        request.Body.Position = 0;
        return SensitiveDataMasker.MaskJson(text);
    }
}
